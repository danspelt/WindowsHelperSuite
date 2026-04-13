using NAudio.CoreAudioApi;

namespace WindowsHelperSuite.Speech.Services;

/// <summary>
/// Detects whether the <b>default</b> playback (render) device looks like headphones/headset — matches
/// "only when headset is default" in settings. Speaking through random Bluetooth endpoints that are not default is not treated as OK.
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
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var name = defaultDevice.FriendlyName ?? "";
            foreach (var keyword in HeadsetKeywords)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
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
