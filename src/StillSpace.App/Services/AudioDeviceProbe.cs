using NAudio.CoreAudioApi;

namespace StillSpace.Services;

public sealed class AudioDeviceProbe
{
    public MMDevice? FindHeadsetPlayback(string? nameSubstring, string? savedDeviceId)
    {
        using var en = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(savedDeviceId))
        {
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (string.Equals(d.ID, savedDeviceId, StringComparison.Ordinal))
                    return d;
            }
        }

        if (string.IsNullOrWhiteSpace(nameSubstring)) return null;
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (d.FriendlyName.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase))
                return d;
        }

        return null;
    }

    public bool AnyRenderDeviceMatches(string? nameSubstring, string? savedDeviceId) =>
        FindHeadsetPlayback(nameSubstring, savedDeviceId) != null;

    public MMDevice GetDefaultRender()
    {
        using var en = new MMDeviceEnumerator();
        return en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    /// <summary>
    /// NAudio’s <see cref="WasapiCapture"/> default uses <see cref="Role.Console"/>; headset / Bluetooth mics are often default only under
    /// <see cref="Role.Communications"/>. Try voice-friendly roles first.
    /// </summary>
    public static MMDevice GetDefaultLiveVoiceCapture()
    {
        using var en = new MMDeviceEnumerator();
        foreach (var role in new[] { Role.Communications, Role.Console, Role.Multimedia })
        {
            try
            {
                return en.GetDefaultAudioEndpoint(DataFlow.Capture, role);
            }
            catch
            {
                /* try next role */
            }
        }

        throw new InvalidOperationException("No default microphone was found for Communications, Console, or Multimedia roles.");
    }

    /// <summary>Find capture endpoint by saved device ID, else friendly name substring (e.g. same match as headset playback).</summary>
    public MMDevice? FindCaptureDevice(string? savedDeviceId, string? nameSubstring)
    {
        using var en = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(savedDeviceId))
        {
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (string.Equals(d.ID, savedDeviceId, StringComparison.Ordinal))
                    return d;
            }
        }

        if (string.IsNullOrWhiteSpace(nameSubstring)) return null;
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            if (d.FriendlyName.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase))
                return d;
        }

        return null;
    }
}
