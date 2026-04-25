using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.CoreAudioApi;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.Speech.LiveCaptions;

/// <summary>
/// Picks a real capture endpoint for Azure Speech on Windows. The SDK default often follows the
/// Multimedia role, which can be Stereo Mix or a silent input; <see cref="FromMicrophoneInput"/>
/// on Windows commonly expects the endpoint <b>ID</b> string (not always the friendly name).
/// </summary>
internal static class LiveCaptionsMicrophoneHelper
{
    private static readonly string[] ExcludedNameSubstrings =
    [
        "stereo mix", "wave out mix", "what u hear", "loopback",
        "line in", "line-in", "microsoft sound mapper",
    ];

    public static AudioConfig CreateAudioConfigForAzure(ILoggingService? log)
    {
        try
        {
            var candidates = CollectCaptureCandidates(log);
            foreach (var (id, name) in candidates)
            {
                if (TryOpen(id, log, "endpoint id"))
                {
                    return AudioConfig.FromMicrophoneInput(id);
                }

                if (!string.IsNullOrWhiteSpace(name) &&
                    !name.Equals(id, StringComparison.Ordinal) &&
                    TryOpen(name, log, "friendly name"))
                {
                    return AudioConfig.FromMicrophoneInput(name);
                }
            }

            log?.Debug("Live Captions (Azure): using Speech SDK default microphone.");
            return AudioConfig.FromDefaultMicrophoneInput();
        }
        catch (Exception ex)
        {
            log?.Warning($"Live Captions (Azure): microphone resolution failed ({ex.Message}); using default input.");
            return AudioConfig.FromDefaultMicrophoneInput();
        }
    }

    private static List<(string Id, string Name)> CollectCaptureCandidates(ILoggingService? log)
    {
        var list = new List<(string Id, string Name)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(MMDevice? device, string source)
        {
            if (device == null)
            {
                return;
            }

            var id = device.ID?.Trim() ?? "";
            var name = device.FriendlyName?.Trim() ?? "";
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (!seen.Add(id))
            {
                return;
            }

            if (!LooksLikeSpeechInput(name))
            {
                log?.Debug($"Live Captions (Azure): skipping capture device ({source}): \"{name}\"");
                return;
            }

            list.Add((id, name));
            log?.Debug($"Live Captions (Azure): capture candidate ({source}): \"{name}\"");
        }

        using var enumerator = new MMDeviceEnumerator();

        foreach (var role in new[] { Role.Communications, Role.Console, Role.Multimedia })
        {
            try
            {
                Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role), $"default {role}");
            }
            catch
            {
                /* try next role */
            }
        }

        try
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                try
                {
                    Add(device, "enumerated");
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            log?.Warning($"Live Captions (Azure): could not enumerate microphones ({ex.Message}).");
        }

        return list;
    }

    private static bool LooksLikeSpeechInput(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return true;
        }

        var n = friendlyName.ToLowerInvariant();
        foreach (var bad in ExcludedNameSubstrings)
        {
            if (n.Contains(bad, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Probe whether the Speech SDK accepts this device string without throwing.</summary>
    private static bool TryOpen(string deviceString, ILoggingService? log, string label)
    {
        if (string.IsNullOrWhiteSpace(deviceString))
        {
            return false;
        }

        try
        {
            using var probe = AudioConfig.FromMicrophoneInput(deviceString);
            return true;
        }
        catch (Exception ex)
        {
            log?.Debug($"Live Captions (Azure): FromMicrophoneInput ({label}) rejected \"{Truncate(deviceString)}\": {ex.Message}");
            return false;
        }
    }

    private static string Truncate(string s, int max = 72) =>
        s.Length <= max ? s : s[..max] + "…";
}
