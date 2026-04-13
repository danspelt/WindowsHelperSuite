using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WindowsHelperSuite.Speech.Services;

/// <summary>
/// Refreshes headset state when the default render device or endpoint list changes (faster than polling alone).
/// </summary>
internal sealed class AudioEndpointChangeNotifier : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Action _onChange;
    private bool _registered;

    public AudioEndpointChangeNotifier(Action onChange)
    {
        _onChange = onChange ?? throw new ArgumentNullException(nameof(onChange));
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(this);
            _registered = true;
        }
        catch
        {
            // COM / audio stack unavailable
        }
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
            /* ignore */
        }

        _registered = false;
        _enumerator.Dispose();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => _onChange();

    public void OnDeviceAdded(string pwstrDeviceId) => _onChange();

    public void OnDeviceRemoved(string deviceId) => _onChange();

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render)
        {
            _onChange();
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
