using WindowsHelperSuite.Writer.Models;
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
}
