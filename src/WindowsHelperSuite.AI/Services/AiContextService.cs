using System.Collections.Concurrent;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

/// <summary>
/// Personal context memory service for smarter AI suggestions.
/// Tracks frequently used phrases, names, and vocabulary.
/// </summary>
public class AiContextService : IAiContextService
{
    private readonly ILoggingService _loggingService;
    private readonly ConcurrentDictionary<string, int> _phraseFrequency = new();
    private readonly ConcurrentDictionary<string, int> _nameFrequency = new();
    private readonly HashSet<string> _knownNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recentPhrases = new();
    private readonly object _lock = new();

    public AiContextService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public void RecordPhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase) || phrase.Length < 3)
            return;

        var normalized = phrase.Trim().ToLowerInvariant();

        _phraseFrequency.AddOrUpdate(normalized, 1, (_, count) => count + 1);

        lock (_lock)
        {
            _recentPhrases.Insert(0, normalized);
            if (_recentPhrases.Count > 100)
            {
                _recentPhrases.RemoveAt(_recentPhrases.Count - 1);
            }
        }

        _loggingService.Debug($"Recorded phrase: {normalized}");
    }

    public IReadOnlyList<string> GetFrequentPhrases(string? context = null, int count = 5)
    {
        var phrases = _phraseFrequency
            .OrderByDescending(kvp => kvp.Value)
            .Take(count)
            .Select(kvp => kvp.Key)
            .ToList();

        if (!string.IsNullOrEmpty(context))
        {
            // Filter phrases that might be relevant to the context
            var contextWords = context.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            phrases = phrases
                .Where(p => contextWords.Any(cw => p.Contains(cw)))
                .Take(count)
                .ToList();
        }

        return phrases;
    }

    public void RecordName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            return;

        // Basic heuristic: names start with capital, aren't common words
        if (!char.IsUpper(name[0]))
            return;

        var normalized = name.Trim();

        lock (_lock)
        {
            _knownNames.Add(normalized);
        }

        _nameFrequency.AddOrUpdate(normalized, 1, (_, count) => count + 1);

        _loggingService.Debug($"Recorded name: {normalized}");
    }

    public IReadOnlyList<string> GetKnownNames()
    {
        lock (_lock)
        {
            return _knownNames.ToList();
        }
    }

    public void ClearContext()
    {
        _phraseFrequency.Clear();
        _nameFrequency.Clear();
        lock (_lock)
        {
            _knownNames.Clear();
            _recentPhrases.Clear();
        }
        _loggingService.Information("AI context memory cleared");
    }
}
