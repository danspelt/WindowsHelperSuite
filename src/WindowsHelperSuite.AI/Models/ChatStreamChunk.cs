namespace WindowsHelperSuite.AI.Models;

public class ChatStreamChunk
{
    public string TextDelta { get; set; } = "";
    public bool IsCompleted { get; set; }
}
