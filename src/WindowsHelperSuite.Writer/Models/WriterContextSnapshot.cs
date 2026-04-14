namespace WindowsHelperSuite.Writer.Models;

public sealed class WriterContextSnapshot
{
    public string ProcessName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    public WriterTypingMode TypingMode { get; init; } = WriterTypingMode.Neutral;
    public bool HeadsetConnected { get; init; }
}
