using System.Text.RegularExpressions;
using System.Windows.Automation;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.Infrastructure.Services;

/// <summary>
/// Layer 1: UIA IsPassword / ValuePattern.IsPassword.
/// Layer 2: name, automation id, class, help text heuristics (no field values).
/// </summary>
public sealed class SecretFieldDetector : ISecretFieldDetector
{
    private static readonly Regex PinWordBoundary = new(@"\bpin\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OtpWordBoundary = new(@"\botp\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public SecretFieldSnapshot GetSnapshot()
    {
        var fg = ForegroundContext.GetWriterSnapshot();
        var processName = fg.ForegroundProcessName;
        var windowTitle = fg.ForegroundWindowTitle;

        try
        {
            var el = AutomationElement.FocusedElement;
            if (el == null)
            {
                return new SecretFieldSnapshot(false, "Unknown", processName, windowTitle, null);
            }

            string controlType;
            try
            {
                controlType = el.Current.ControlType?.ProgrammaticName ?? "";
            }
            catch
            {
                controlType = "";
            }

            if (TryGetUiPassword(el, out var uiaReason))
            {
                return new SecretFieldSnapshot(true, uiaReason, processName, windowTitle, controlType);
            }

            if (TryHeuristicMetadata(el, out var hReason))
            {
                return new SecretFieldSnapshot(true, hReason, processName, windowTitle, controlType);
            }

            if (TryAppWindowHeuristic(processName, windowTitle, out var wReason))
            {
                return new SecretFieldSnapshot(true, wReason, processName, windowTitle, controlType);
            }

            return new SecretFieldSnapshot(false, "Unknown", processName, windowTitle, controlType);
        }
        catch (Exception ex)
        {
            return new SecretFieldSnapshot(false, $"DetectorError:{ex.GetType().Name}", processName, windowTitle, null);
        }
    }

    private static bool TryGetUiPassword(AutomationElement el, out string reason)
    {
        reason = "";
        try
        {
            var v = el.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
            if (v is bool b && b)
            {
                reason = "UIA:IsPassword";
                return true;
            }
        }
        catch
        {
            // Property may be unsupported
        }

        return false;
    }

    private static bool TryHeuristicMetadata(AutomationElement el, out string reason)
    {
        reason = "";
        string name;
        string aid;
        string cls;
        string help;
        try
        {
            name = el.Current.Name ?? "";
            aid = el.Current.AutomationId ?? "";
            cls = el.Current.ClassName ?? "";
        }
        catch
        {
            return false;
        }

        try
        {
            var h = el.GetCurrentPropertyValue(AutomationElement.HelpTextProperty);
            help = h as string ?? "";
        }
        catch
        {
            help = "";
        }

        var combined = $"{name} {aid} {cls} {help}";
        var lower = combined.ToLowerInvariant();

        // Strong substrings (avoid bare "pin" without word boundary)
        string[] strong =
        [
            "password", "passcode", "passwd", "secret", "token", "api key", "apikey",
            "one-time", "verification code", "credential", "cvv", "ssn", "mfa", "2fa",
            "access token", "client secret", "bearer", "otp", "authenticator"
        ];

        foreach (var k in strong)
        {
            if (lower.Contains(k))
            {
                reason = $"Heuristic:substring={k}";
                return true;
            }
        }

        if (PinWordBoundary.IsMatch(combined) || OtpWordBoundary.IsMatch(combined))
        {
            reason = "Heuristic:wordBoundary=pin|otp";
            return true;
        }

        return false;
    }

    /// <summary>Conservative window-level hints when control metadata is weak (e.g. some browser hosts).</summary>
    private static bool TryAppWindowHeuristic(string? processName, string? windowTitle, out string reason)
    {
        reason = "";
        if (string.IsNullOrEmpty(windowTitle))
        {
            return false;
        }

        var p = (processName ?? "").ToLowerInvariant();
        var isBrowser = p is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi";
        if (!isBrowser)
        {
            return false;
        }

        var t = windowTitle.ToLowerInvariant();
        // Auth-ish titles — suppress Writer for the whole window when title strongly suggests login
        if (t.Contains("sign in") || t.Contains("sign-in") || t.Contains("log in") || t.Contains("log-in") ||
            t.Contains("login") || t.Contains("enter your password") || t.Contains("authentication") ||
            t.Contains("verify your identity") || t.Contains("two-factor") || t.Contains("2-step"))
        {
            reason = "AppRule:browserAuthTitle";
            return true;
        }

        return false;
    }
}
