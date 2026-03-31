namespace WindowsHelperSuite.Core.Models.Writer;

/// <summary>Result of secret-field detection — never includes field values or typed text.</summary>
public sealed record SecretFieldSnapshot(
    bool IsProtected,
    string Reason,
    string? ProcessName,
    string? WindowTitle,
    string? ControlType);
