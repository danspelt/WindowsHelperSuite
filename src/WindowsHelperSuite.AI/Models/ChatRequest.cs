namespace WindowsHelperSuite.AI.Models;

public class ChatRequest
{
    public string Model { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public IReadOnlyList<ChatMessage> Messages { get; set; } = Array.Empty<ChatMessage>();
    public double Temperature { get; set; } = 0.7;
}
