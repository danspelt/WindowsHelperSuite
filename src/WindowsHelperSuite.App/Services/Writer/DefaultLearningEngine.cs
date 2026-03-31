using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.App.Services.Writer;

/// <summary>No-op extension hooks; replace with adaptive / typo-map learning when ready.</summary>
public sealed class DefaultLearningEngine : ILearningEngine
{
    public void OnWordCommitted(string word, string? textBeforeWord, WriterContextSnapshot context)
    {
    }

    public void OnSentenceCompleted(string sentence, WriterContextSnapshot context)
    {
    }

    public void OnSuggestionAccepted(string acceptedText, WriterContextSnapshot context)
    {
    }
}
