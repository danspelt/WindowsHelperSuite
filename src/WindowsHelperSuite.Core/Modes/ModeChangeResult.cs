namespace WindowsHelperSuite.Core.Modes;

public sealed class ModeChangeResult
{
    public bool Success { get; init; }
    public AppMode PreviousMode { get; init; }
    public AppMode NewMode { get; init; }
    public string? ErrorMessage { get; init; }
}
