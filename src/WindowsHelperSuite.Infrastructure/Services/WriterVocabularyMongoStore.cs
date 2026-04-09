using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using WindowsHelperSuite.Core.Interfaces;
using WindowsHelperSuite.Core.Models.Settings;
using WindowsHelperSuite.Core.Models.Writer;

namespace WindowsHelperSuite.Infrastructure.Services;

/// <summary>No-op when Mongo sync is disabled or connection string is missing.</summary>
public sealed class NullWriterVocabularyRemoteStore : IWriterVocabularyRemoteStore
{
    public static readonly NullWriterVocabularyRemoteStore Instance = new();

    private NullWriterVocabularyRemoteStore() { }

    public bool IsEnabled => false;

    public Task UpsertAsync(
        string text,
        bool isPhrase,
        string? sentenceContext,
        WriterTypingMode writerMode,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

[BsonIgnoreExtraElements]
internal sealed class WriterVocabularyDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public string Kind { get; set; } = "";

    public string LastSentenceContext { get; set; } = "";

    public string WriterMode { get; set; } = "";

    public long SeenCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Upserts learned words/phrases with the current sentence buffer as context.</summary>
public sealed class WriterVocabularyMongoStore : IWriterVocabularyRemoteStore
{
    private const int MaxContextChars = 8000;

    private readonly MongoVocabularySettings _settings;
    private readonly ILoggingService _log;
    private readonly MongoClient _client;
    private readonly IMongoCollection<WriterVocabularyDocument> _collection;

    public bool IsEnabled => true;

    private WriterVocabularyMongoStore(MongoVocabularySettings settings, ILoggingService log, MongoClient client)
    {
        _settings = settings;
        _log = log;
        _client = client;
        var db = client.GetDatabase(settings.DatabaseName);
        _collection = db.GetCollection<WriterVocabularyDocument>(settings.CollectionName);
    }

    public static IWriterVocabularyRemoteStore Create(MongoVocabularySettings settings, ILoggingService log)
    {
        if (!settings.EnableSync || string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            return NullWriterVocabularyRemoteStore.Instance;
        }

        try
        {
            var client = new MongoClient(settings.ConnectionString);
            return new WriterVocabularyMongoStore(settings, log, client);
        }
        catch (Exception ex)
        {
            log.Warning($"Mongo vocabulary: could not create client — {ex.Message}");
            return NullWriterVocabularyRemoteStore.Instance;
        }
    }

    public async Task UpsertAsync(
        string text,
        bool isPhrase,
        string? sentenceContext,
        WriterTypingMode writerMode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var normalized = text.Trim();
        if (normalized.Length <= 1 && !isPhrase)
        {
            return;
        }

        var id = BuildId(normalized, isPhrase);
        var ctx = TruncateContext(sentenceContext);

        try
        {
            var filter = Builders<WriterVocabularyDocument>.Filter.Eq(d => d.Id, id);
            var update = Builders<WriterVocabularyDocument>.Update
                .Set(d => d.Text, normalized)
                .Set(d => d.Kind, isPhrase ? "phrase" : "word")
                .Set(d => d.LastSentenceContext, ctx)
                .Set(d => d.WriterMode, writerMode.ToString())
                .Set(d => d.UpdatedAtUtc, DateTime.UtcNow)
                .Inc(d => d.SeenCount, 1);

            await _collection.UpdateOneAsync(
                    filter,
                    update,
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warning($"Mongo vocabulary upsert failed for '{id}': {ex.Message}");
        }
    }

    private static string BuildId(string normalized, bool isPhrase) =>
        (isPhrase ? "p:" : "w:") + normalized.ToLowerInvariant();

    private static string TruncateContext(string? sentenceContext)
    {
        if (string.IsNullOrEmpty(sentenceContext))
        {
            return "";
        }

        var s = sentenceContext.Trim();
        return s.Length <= MaxContextChars ? s : s[^MaxContextChars..];
    }
}
