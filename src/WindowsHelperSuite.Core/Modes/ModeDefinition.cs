namespace WindowsHelperSuite.Core.Modes;

/// <summary>
/// Describes subsystem toggles for a mode (documentation + future automation).
/// </summary>
public sealed class ModeDefinition
{
    public AppMode Mode { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool EnableWriterOverlay { get; init; }
    public bool EnablePrediction { get; init; }
    public bool EnableSpeechAssist { get; init; }
    public bool EnableGlobalHotkeys { get; init; }
    public bool EnableSystemActions { get; init; }

    public static ModeDefinition For(AppMode mode) => mode switch
    {
        AppMode.Writer => new ModeDefinition
        {
            Mode = AppMode.Writer,
            DisplayName = "Writer Mode",
            EnableWriterOverlay = true,
            EnablePrediction = true,
            EnableSpeechAssist = true,
            EnableGlobalHotkeys = true,
            EnableSystemActions = false
        },
        _ => new ModeDefinition
        {
            Mode = AppMode.Hotkey,
            DisplayName = "Hotkey Mode",
            EnableWriterOverlay = false,
            EnablePrediction = false,
            EnableSpeechAssist = false,
            EnableGlobalHotkeys = true,
            EnableSystemActions = true
        }
    };
}
