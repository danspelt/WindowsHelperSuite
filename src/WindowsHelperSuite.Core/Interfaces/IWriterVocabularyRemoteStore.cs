using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Core.Interfaces;

/// <summary>Pushes learned words/phrases plus recent sentence context to a remote store (e.g. MongoDB).</summary>
public interface IWriterVocabularyRemoteStore
{
    bool IsEnabled { get; }

    Task UpsertAsync(
        string text,
        bool isPhrase,
        string? sentenceContext,
        WriterTypingMode writerMode,
        CancellationToken cancellationToken = default);
}
