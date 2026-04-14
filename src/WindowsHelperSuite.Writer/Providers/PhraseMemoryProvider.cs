using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Storage;

namespace WindowsHelperSuite.Writer.Providers;

public sealed class PhraseMemoryProvider : IPredictionProvider
{
    private readonly ITypingModel _typingModel;
    private readonly UserPhraseStore? _extraPhrases;

    public PhraseMemoryProvider(ITypingModel typingModel, UserPhraseStore? extraPhrases = null)
    {
        _typingModel = typingModel;
        _extraPhrases = extraPhrases;
    }

    public string Name => "phrase-memory";

    public Task<IReadOnlyList<PredictionCandidate>> GetCandidatesAsync(
        PredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = request.CurrentToken?.Trim() ?? "";
        var prev = request.PreviousCompletedWord?.Trim() ?? "";

        var results = new List<PredictionCandidate>();

        if (!string.IsNullOrWhiteSpace(token))
        {
            foreach (var w in _typingModel.GetWords(token))
            {
                if (string.IsNullOrWhiteSpace(w.Word))
                {
                    continue;
                }

                results.Add(new PredictionCandidate
                {
                    Text = w.Word,
                    Source = Name,
                    BaseScore = 4.0 + Math.Log(1 + Math.Max(1, w.Count)),
                    IsPhrase = false
                });
            }

            foreach (var p in _typingModel.GetPhrases(token))
            {
                if (string.IsNullOrWhiteSpace(p.Phrase))
                {
                    continue;
                }

                results.Add(new PredictionCandidate
                {
                    Text = p.Phrase,
                    Source = Name,
                    BaseScore = 5.0 + Math.Log(1 + Math.Max(1, p.Count)),
                    IsPhrase = true
                });
            }

            // Phrases that follow the *previous* completed word (sentence already has words before the partial).
            if (!string.IsNullOrWhiteSpace(prev) &&
                !prev.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var p in _typingModel.GetPhrases(prev))
                {
                    if (string.IsNullOrWhiteSpace(p.Phrase))
                    {
                        continue;
                    }

                    results.Add(new PredictionCandidate
                    {
                        Text = p.Phrase,
                        Source = Name,
                        BaseScore = 5.15 + Math.Log(1 + Math.Max(1, p.Count)),
                        IsPhrase = true
                    });
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(prev))
        {
            foreach (var p in _typingModel.GetPhrases(prev))
            {
                if (string.IsNullOrWhiteSpace(p.Phrase))
                {
                    continue;
                }

                results.Add(new PredictionCandidate
                {
                    Text = p.Phrase,
                    Source = Name,
                    BaseScore = 5.2 + Math.Log(1 + Math.Max(1, p.Count)),
                    IsPhrase = true
                });
            }
        }

        if (_extraPhrases is not null)
        {
            foreach (var row in _extraPhrases.Snapshot())
            {
                if (string.IsNullOrWhiteSpace(row.Text))
                {
                    continue;
                }

                var match =
                    (!string.IsNullOrWhiteSpace(token) &&
                     row.Text.StartsWith(token, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(prev) &&
                     row.Text.StartsWith(prev + " ", StringComparison.OrdinalIgnoreCase));

                if (!match)
                {
                    continue;
                }

                results.Add(new PredictionCandidate
                {
                    Text = row.Text,
                    Source = Name,
                    BaseScore = 5.5 + Math.Log(1 + Math.Max(1, row.Count)),
                    IsPhrase = row.Text.Contains(' ', StringComparison.Ordinal)
                });
            }
        }

        return Task.FromResult<IReadOnlyList<PredictionCandidate>>(results);
    }
}
