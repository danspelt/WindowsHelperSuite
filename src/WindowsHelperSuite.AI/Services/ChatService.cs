using System.Runtime.CompilerServices;
using WindowsHelperSuite.AI.Contracts;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

public class ChatService : IChatService
{
    private readonly IChatProvider _provider;
    private readonly ILoggingService _log;

    public ChatService(IChatProvider provider, ILoggingService log)
    {
        _provider = provider;
        _log = log;
    }

    public async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct = default)
    {
        _log.Information($"[ChatService] Send → {request.Messages.Count} messages");
        return await _provider.SendAsync(request, ct);
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _log.Information($"[ChatService] Stream → {request.Messages.Count} messages");
        await foreach (var chunk in _provider.StreamAsync(request, ct))
        {
            yield return chunk;
        }
    }

    public async Task<ChatResponse> TestConnectionAsync(CancellationToken ct = default)
    {
        _log.Information("[ChatService] Testing connection…");
        var request = new ChatRequest
        {
            Model = "",
            SystemPrompt = "Reply with exactly: OK",
            Messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = "Say hello." }
            },
            Temperature = 0,
        };
        return await _provider.SendAsync(request, ct);
    }
}
