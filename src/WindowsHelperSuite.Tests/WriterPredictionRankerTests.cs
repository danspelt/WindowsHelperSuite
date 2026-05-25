using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Prediction.Services;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Providers;
using WindowsHelperSuite.Writer.Ranking;

namespace WindowsHelperSuite.Tests;

public sealed class WriterPredictionRankerTests
{
    [Fact]
    public void Rank_prefers_phrase_memory_boost_over_plain_prefix_when_scores_close()
    {
        var ranker = new CandidateRanker();
        var request = new PredictionRequest
        {
            CurrentToken = "talk",
            Context = new WriterContextSnapshot { TypingMode = WriterTypingMode.Chat },
            MaxSuggestions = 5
        };

        var candidates = new[]
        {
            new PredictionCandidate { Text = "talkative", Source = "prefix-word", BaseScore = 2.0 },
            new PredictionCandidate { Text = "talk to you", Source = "phrase-memory", BaseScore = 2.1, IsPhrase = true }
        };

        var ranked = ranker.Rank(request, candidates).ToList();

        Assert.Equal("talk to you", ranked[0].Text);
    }

    [Fact]
    public void Rank_after_space_boosts_single_word_over_phrase()
    {
        var ranker = new CandidateRanker();
        var request = new PredictionRequest
        {
            CurrentToken = "",
            PreviousCompletedWord = "how",
            Context = new WriterContextSnapshot { TypingMode = WriterTypingMode.Neutral },
            MaxSuggestions = 5
        };

        var candidates = new[]
        {
            new PredictionCandidate { Text = "how are you", Source = "phrase-memory", BaseScore = 5.5, IsPhrase = true },
            new PredictionCandidate { Text = "are", Source = "next-word", BaseScore = 4.0, IsPhrase = false }
        };

        var ranked = ranker.Rank(request, candidates).ToList();

        Assert.Equal("are", ranked[0].Text);
    }

    [Fact]
    public void MergeSuggestionLists_postSpace_puts_word_bank_next_words_first()
    {
        var bank = new List<SuggestionItem>
        {
            new() { DisplayText = "are", Kind = SuggestionKind.NextWord, Score = 3000, InsertText = "are" },
            new() { DisplayText = "how are you", Kind = SuggestionKind.PhraseCompletion, Score = 2500, InsertText = "are you" }
        };
        var engine = new List<SuggestionItem>
        {
            new() { DisplayText = "can", Kind = SuggestionKind.WordCompletion, Score = 2800, InsertText = "can" },
            new() { DisplayText = "how do you do", Kind = SuggestionKind.PhraseCompletion, Score = 2600, InsertText = "do you do" }
        };

        var merged = CompositePredictionService.MergeSuggestionLists(engine, bank, maxSlots: 4, postSpace: true);

        Assert.True(merged.Count >= 2);
        Assert.Equal("are", merged[0].DisplayText);
        Assert.Contains(merged, s => s.DisplayText.Equals("can", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class NextWordProviderTests
{
    private sealed class StubLookup : Core.Interfaces.INextWordLookup
    {
        public IReadOnlyList<Core.Models.Writer.NextWordCandidate> GetNextWordsAfter(string lastWord, string? wordBeforeLast = null) =>
            lastWord.Equals("how", StringComparison.OrdinalIgnoreCase)
                ? [new("are", 5.0), new("do", 4.0)]
                : [];
    }

    [Fact]
    public async Task GetCandidatesAsync_returns_next_words_when_token_empty()
    {
        var provider = new NextWordProvider(new StubLookup());
        var request = new PredictionRequest
        {
            CurrentToken = "",
            PreviousCompletedWord = "how",
            CurrentSentence = "how"
        };

        var results = await provider.GetCandidatesAsync(request);
        Assert.Contains(results, c => c.Text == "are");
        Assert.Contains(results, c => c.Text == "do");
    }
}
