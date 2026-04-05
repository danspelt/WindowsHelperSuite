using System.Text.RegularExpressions;
using System.Windows.Automation;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Infrastructure.Hooks;

namespace WindowsHelperSuite.Infrastructure.Services;

/// <summary>
/// Suppresses Writer on browser omnibox / URL fields (Chromium, Firefox) using UIA metadata only.
/// </summary>
public sealed class WriterOverlayExclusionDetector : IWriterOverlayExclusionDetector
{
    private static readonly Regex ChromiumOmniboxAutomationId = new(@"^view_\d+$", RegexOptions.CultureInvariant);

    public bool ShouldExcludeWriterOverlay(out string reason)
    {
        reason = "";
        try
        {
            var fg = ForegroundContext.GetWriterSnapshot();
            var processName = (fg.ForegroundProcessName ?? "").ToLowerInvariant();

            var el = AutomationElement.FocusedElement;
            if (el == null)
            {
                return false;
            }

            string name;
            string aid;
            string cls;
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

            if (IsChromiumOmnibox(processName, name, aid, cls))
            {
                reason = "Browser:ChromiumOmnibox";
                return true;
            }

            if (IsFirefoxUrlBar(processName, aid, cls, name))
            {
                reason = "Browser:FirefoxUrlBar";
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsChromiumBrowser(string processName) =>
        processName is "chrome" or "msedge" or "brave" or "opera" or "vivaldi" or "arc";

    private static bool IsChromiumOmnibox(string processName, string name, string aid, string cls)
    {
        if (!IsChromiumBrowser(processName))
        {
            return false;
        }

        if (cls.Contains("OmniboxView", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ChromiumOmniboxAutomationId.IsMatch(aid))
        {
            return true;
        }

        // English / many locales: "Address and search bar"
        if (name.Contains("Address", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsFirefoxUrlBar(string processName, string aid, string cls, string name)
    {
        if (processName != "firefox")
        {
            return false;
        }

        if (aid.Equals("urlbar-input", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cls.Contains("searchbar-textbox", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("Search", StringComparison.OrdinalIgnoreCase) &&
            (name.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("address", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
