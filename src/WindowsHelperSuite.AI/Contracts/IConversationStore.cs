using WindowsHelperSuite.AI.Models;

namespace WindowsHelperSuite.AI.Contracts;

public interface IConversationStore
{
    Task SaveAsync(ChatConversation conversation);
    Task<ChatConversation?> LoadAsync(string conversationId);
    Task<IReadOnlyList<ChatConversation>> GetRecentAsync(int maxCount = 50);
    Task DeleteAsync(string conversationId);
}
