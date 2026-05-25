using System.Net.Http;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Writer.Abstractions;
using WindowsHelperSuite.Writer.Config;
using WindowsHelperSuite.Writer.Llm;
using WindowsHelperSuite.Writer.Providers;
using WindowsHelperSuite.Writer.Ranking;
using WindowsHelperSuite.Writer.Storage;

namespace WindowsHelperSuite.Writer.Services;

public static class WriterPredictionBootstrap
{
    /// <summary>Provider order: phrase memory, prefix dictionary, recency, corrections, optional local LLM.</summary>
    public static global::WindowsHelperSuite.Writer.Abstractions.IPredictionService CreateDefaultEngine(
        ITypingModel typingModel,
        JsonTypingModelStore stores,
        LocalLlmOptions llmOptions,
        HttpClient httpClient,
        WriterPredictionOptions? rankOptions = null,
        INextWordLookup? nextWordLookup = null)
    {
        var frequencies = WordFrequencyLoader.LoadDefault();
        var llmClient = new LocalLlmClient(httpClient, llmOptions);

        var providers = new List<IPredictionProvider>
        {
            new PhraseMemoryProvider(typingModel, stores.Phrases),
        };

        if (nextWordLookup is not null)
        {
            providers.Add(new NextWordProvider(nextWordLookup));
        }

        providers.AddRange(
        [
            new PrefixWordProvider(frequencies),
            new RecencyProvider(stores.Words, typingModel),
            new CorrectionProvider(typingModel),
            new LocalLlmProvider(llmClient, llmOptions)
        ]);

        var ranker = new CandidateRanker(rankOptions);
        return new PredictionService(providers, ranker);
    }
}
