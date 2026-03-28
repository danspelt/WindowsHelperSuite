using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Modes;

namespace WindowsHelperSuite.App.Services;

/// <summary>
/// Coordinates persisted app mode and applies infrastructure toggles via <paramref name="applyMode"/>.
/// </summary>
public sealed class ModeManager : IModeManager
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly Action<AppMode> _applyMode;
    private AppMode _current;

    public ModeManager(
        ISettingsService settingsService,
        ILoggingService loggingService,
        Action<AppMode> applyMode)
    {
        _settingsService = settingsService;
        _loggingService = loggingService;
        _applyMode = applyMode;
    }

    public AppMode CurrentMode => _current;

    public event EventHandler<AppMode>? ModeChanged;

    public void Initialize()
    {
        var ms = _settingsService.Settings.ModeSystem;
        _current = ms.RememberLastMode ? ms.CurrentMode : AppMode.Hotkey;

        try
        {
            _applyMode(_current);
        }
        catch (Exception ex)
        {
            _loggingService.Error("Mode initialization failed", ex);
        }

        _loggingService.Information($"Mode restored on startup: {ModeDefinition.For(_current).DisplayName}");
    }

    public ModeChangeResult SwitchMode(AppMode newMode)
    {
        var previous = _current;
        if (previous == newMode)
        {
            return new ModeChangeResult
            {
                Success = true,
                PreviousMode = newMode,
                NewMode = newMode
            };
        }

        try
        {
            _applyMode(newMode);
            _current = newMode;

            if (_settingsService.Settings.ModeSystem.RememberLastMode)
            {
                _settingsService.Settings.ModeSystem.CurrentMode = newMode;
                try
                {
                    _settingsService.Save();
                }
                catch (Exception ex)
                {
                    _loggingService.Warning($"Could not save mode to settings: {ex.Message}");
                }
            }

            _loggingService.Information($"Mode switch: {ModeDefinition.For(previous).DisplayName} → {ModeDefinition.For(newMode).DisplayName}");
            ModeChanged?.Invoke(this, newMode);

            return new ModeChangeResult
            {
                Success = true,
                PreviousMode = previous,
                NewMode = newMode
            };
        }
        catch (Exception ex)
        {
            _loggingService.Error($"Mode switch failed ({previous} → {newMode})", ex);
            try
            {
                _applyMode(previous);
                _current = previous;
            }
            catch (Exception rollbackEx)
            {
                _loggingService.Error("Mode rollback failed", rollbackEx);
            }

            return new ModeChangeResult
            {
                Success = false,
                PreviousMode = previous,
                NewMode = newMode,
                ErrorMessage = ex.Message
            };
        }
    }
}
