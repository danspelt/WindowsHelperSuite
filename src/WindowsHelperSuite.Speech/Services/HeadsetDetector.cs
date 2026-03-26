using NAudio.CoreAudioApi;

namespace WindowsHelperSuite.Speech.Services;

/// <summary>
/// Detects an active render device that looks like a headset (privacy / UX gate).
/// </summary>
public sealed class HeadsetDetector
{
    private static readonly string[] HeadsetKeywords =
    [
        "headset", "headphone", "earphone", "earbuds",
        "shokz", "bluetooth", "airpods", "jabra",
        "bose", "sony wh", "sony wf", "galaxy buds",
        "bt audio", "hands-free"
    ];

    public bool IsHeadsetConnected()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in devices)
            {
                var name = device.FriendlyName?.ToLowerInvariant() ?? "";
                foreach (var keyword in HeadsetKeywords)
                {
                    if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Never fail callers; treat as no headset match
        }

        return false;
    }
}
