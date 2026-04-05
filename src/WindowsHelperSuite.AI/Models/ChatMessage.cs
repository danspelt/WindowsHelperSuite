namespace WindowsHelperSuite.AI.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = ""; // system, user, assistant
    public string Content { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
