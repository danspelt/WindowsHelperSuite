using System.Collections.Concurrent;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Services;

public sealed class SuggestionCache
{
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, (PredictionResult Result, DateTimeOffset Until)> _entries = new();

    public SuggestionCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromMilliseconds(400);
    }

    public bool TryGet(in PredictionRequest request, out PredictionResult? result)
    {
        var key = BuildKey(request);
        if (_entries.TryGetValue(key, out var row) && row.Until > DateTimeOffset.UtcNow)
        {
            result = row.Result;
            return true;
        }

        result = null;
        return false;
    }

    public void Set(in PredictionRequest request, PredictionResult result)
    {
        var key = BuildKey(request);
        _entries[key] = (result, DateTimeOffset.UtcNow.Add(_ttl));
        PruneStale();
    }

    private void PruneStale()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _entries)
        {
            if (kv.Value.Until < now)
            {
                _entries.TryRemove(kv.Key, out _);
            }
        }
    }

    private static string BuildKey(PredictionRequest request)
    {
        var mode = request.Context.TypingMode.ToString();
        var proc = request.Context.ProcessName ?? "";
        return $"{request.CurrentToken}\u001f{request.CurrentSentence}\u001f{mode}\u001f{proc}";
    }
}
