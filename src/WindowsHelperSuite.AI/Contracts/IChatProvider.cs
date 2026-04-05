using WindowsHelperSuite.AI.Models;

namespace WindowsHelperSuite.AI.Contracts;

public interface IChatProvider
{
    string Name { get; }
    Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ChatStreamChunk> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
