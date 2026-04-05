namespace WindowsHelperSuite.AI.Models;

public class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
