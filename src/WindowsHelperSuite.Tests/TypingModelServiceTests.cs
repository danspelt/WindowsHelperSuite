using FluentAssertions;
using WindowsHelperSuite.Core.Models;
using WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Prediction.Services;

namespace WindowsHelperSuite.Tests;

public sealed class TypingModelServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public TypingModelServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "whs-typing-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "writer-model.json");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // Temp cleanup best-effort
        }
    }

    private static WriterContextSnapshot Ctx => WriterContextSnapshot.Neutral;

    [Fact]
    public void RecordWord_increments_count()
    {
        using var m = new TypingModelService(_path);
        m.RecordWord("hello", Ctx);
        m.RecordWord("hello", Ctx);
        m.GetWords("hel").Should().ContainSingle(w => w.Word == "hello" && w.Count == 2);
    }

    [Fact]
    public void Save_and_reload_roundtrip_preserves_data()
    {
        using (var m = new TypingModelService(_path))
        {
            m.RecordWord("persisted", Ctx);
            m.Save();
        }

        using (var m2 = new TypingModelService(_path))
        {
            m2.GetWords("pers").Should().ContainSingle(w => w.Word == "persisted" && w.Count == 1);
        }
    }

    [Fact]
    public void RecordPhrase_stores_under_prefix()
    {
        using var m = new TypingModelService(_path);
        m.RecordPhrase("how are you", Ctx);
        m.GetPhrases("how").Should().Contain(p => p.Phrase == "how are you");
    }

    [Fact]
    public void RecordCorrection_exact_lookup()
    {
        using var m = new TypingModelService(_path);
        m.RecordCorrection("ths", "this");
        m.GetCorrection("ths").Should().Be("this");
    }

    [Fact]
    public void GetCorrectionMatches_prefix()
    {
        using var m = new TypingModelService(_path);
        m.RecordCorrection("teh", "the");
        m.GetCorrectionMatches("t").Should().Contain(c => c.Typed == "teh" && c.Corrected == "the");
    }

    [Fact]
    public void GetRankingBoost_positive_for_known_word()
    {
        using var m = new TypingModelService(_path);
        m.RecordWord("boostword", Ctx);
        m.GetRankingBoost("boostword", SuggestionKind.WordCompletion, Ctx).Should().BeGreaterThan(0);
    }
}
