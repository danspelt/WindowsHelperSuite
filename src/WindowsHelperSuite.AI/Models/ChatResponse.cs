namespace WindowsHelperSuite.AI.Models;

public class ChatResponse
{
    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
