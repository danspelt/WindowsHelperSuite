using WindowsHelperSuite.AI.Models;

namespace WindowsHelperSuite.AI.Contracts;

public interface IChatService
{
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
    Task<ChatResponse> TestConnectionAsync(CancellationToken cancellationToken = default);
}
