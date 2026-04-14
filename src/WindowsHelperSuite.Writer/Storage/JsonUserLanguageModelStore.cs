using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Storage;

public sealed class JsonUserLanguageModelStore : IUserLanguageModelStore, IDisposable
{
    private readonly JsonTypingModelStore _store;

    public JsonUserLanguageModelStore(JsonTypingModelStore? store = null)
    {
        _store = store ?? new JsonTypingModelStore();
    }

    public Task RecordAcceptedSuggestionAsync(
        string text,
        WriterContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (t.Contains(' ', StringComparison.Ordinal))
        {
            _store.Phrases.BumpPhrase(t, context);
        }
        else
        {
            _store.Words.RecordAcceptedWord(t, context);
        }

        return Task.CompletedTask;
    }

    public Task RecordCommittedTextAsync(
        string text,
        WriterContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        var t = text.Trim();
        if (t.Length < 4)
        {
            return Task.CompletedTask;
        }

        if (t.Contains(' ', StringComparison.Ordinal))
        {
            _store.Phrases.BumpPhrase(t, context);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _store.Dispose();
}
