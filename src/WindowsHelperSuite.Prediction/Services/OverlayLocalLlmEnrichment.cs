using CoreWriter = WindowsHelperSuite.Core.Models.Writer;
using WindowsHelperSuite.Writer.Llm;
using WindowsHelperSuite.Writer.Models;
using WindowsHelperSuite.Writer.Providers;

namespace WindowsHelperSuite.Prediction.Services;

/// <summary>Fetches overlay suggestion lines from a local OpenAI-compatible server (e.g. LM Studio) without a cloud API key.</summary>
public static class OverlayLocalLlmEnrichment
{
    public static async Task<IReadOnlyList<string>> FetchSuggestionLinesAsync(
        HttpClient http,
        LocalLlmOptions options,
        string lineForModel,
        string suggestionContextPrefix,
        string currentWord,
        CoreWriter.WriterContextSnapshot coreContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lineForModel) && string.IsNullOrWhiteSpace(currentWord))
        {
            return [];
        }

        var engineCtx = WriterEngineSnapshotMapper.ToWriterEngine(coreContext);
        var prev = WriterSentenceContext.LastCompletedWord(suggestionContextPrefix ?? "");
        var request = new PredictionRequest
        {
            CurrentSentence = string.IsNullOrWhiteSpace(lineForModel)
                ? currentWord
                : lineForModel.Trim(),
            PreviousCompletedWord = prev,
            CurrentToken = currentWord ?? "",
            Context = engineCtx,
            MaxSuggestions = Math.Clamp(options.MaxSuggestions, 1, 9)
        };

        var client = new LocalLlmClient(http, options);
        var provider = new LocalLlmProvider(client, options);
        var candidates = await provider.GetCandidatesAsync(request, cancellationToken).ConfigureAwait(false);
        return candidates.Select(c => c.Text.Trim()).Where(t => t.Length > 0).ToList();
    }
}
