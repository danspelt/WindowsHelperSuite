using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>Hooks for adaptive typo maps, phrase analytics, and personal models beyond <see cref="IPredictionService"/>.</summary>
public interface ILearningEngine
{
    void OnWordCommitted(string word, string? textBeforeWord, WriterContextSnapshot context);

    void OnSentenceCompleted(string sentence, WriterContextSnapshot context);

    void OnSuggestionAccepted(string acceptedText, WriterContextSnapshot context);
}
