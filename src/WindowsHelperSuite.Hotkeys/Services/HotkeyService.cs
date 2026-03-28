using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Infrastructure.Audio;
using WindowsHelperSuite.Infrastructure.Services;

namespace WindowsHelperSuite.Hotkeys.Services;

public class HotkeyService : IHotkeyService, IDisposable
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly ILoggingService _loggingService;
    private readonly Dictionary<string, Action> _actionHandlers;

    public event EventHandler<string>? HotkeyPressed;

    public HotkeyService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
        _keyboardHook = new KeyboardHookService(loggingService);
        _actionHandlers = new Dictionary<string, Action>();

        RegisterDefaultActions();
        _keyboardHook.HotkeyPressed += OnHotkeyPressed;
    }

    public void Start()
    {
        _keyboardHook.StartHook();
        _loggingService.Information("Hotkey service started");
    }

    public void Stop()
    {
        _keyboardHook.StopHook();
        _loggingService.Information("Hotkey service stopped");
    }

    public void RegisterHotkey(string actionName, string gesture, bool consumeMatchingKeys = false)
    {
        _keyboardHook.RegisterHotkey(actionName, gesture, consumeMatchingKeys);
        _loggingService.Information($"Registered hotkey: {actionName} = {gesture}" + (consumeMatchingKeys ? " (consume keys)" : ""));
    }

    public void UnregisterHotkey(string actionName)
    {
        _keyboardHook.UnregisterHotkey(actionName);
    }

    public void RegisterAction(string actionName, Action handler)
    {
        _actionHandlers[actionName] = handler;
    }

    private void RegisterDefaultActions()
    {
        _actionHandlers["VolumeUp"] = () =>
        {
            Win32Audio.VolumeUp();
            _loggingService.Information("Volume increased");
        };

        _actionHandlers["VolumeDown"] = () =>
        {
            Win32Audio.VolumeDown();
            _loggingService.Information("Volume decreased");
        };

        _actionHandlers["VolumeMute"] = () =>
        {
            Win32Audio.VolumeMute();
            _loggingService.Information("Volume muted/unmuted");
        };

        _actionHandlers["WriterRefresh"] = () =>
        {
            _loggingService.Information("Writer refresh requested");
        };

        _actionHandlers["ToggleOverlay"] = () =>
        {
            _loggingService.Information("Overlay toggle requested");
        };

        _actionHandlers["PauseWriter"] = () =>
        {
            _loggingService.Information("Writer pause requested");
        };
    }

    private void OnHotkeyPressed(object? sender, string actionName)
    {
        _loggingService.Information($"Hotkey pressed: {actionName}");

        if (_actionHandlers.TryGetValue(actionName, out var handler))
        {
            handler.Invoke();
        }

        HotkeyPressed?.Invoke(this, actionName);
    }

    public void Dispose()
    {
        _keyboardHook.Dispose();
        GC.SuppressFinalize(this);
    }
}
