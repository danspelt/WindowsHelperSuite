namespace WindowsHelperSuite.Writer.Storage;

/// <summary>Coordinates on-disk JSON stores used for personalized ranking (phrases + accepted-word recency).</summary>
public sealed class JsonTypingModelStore : IDisposable
{
    public JsonTypingModelStore(string? rootDirectory = null)
    {
        var dir = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "writer-ai");

        Directory.CreateDirectory(dir);
        Phrases = new UserPhraseStore(Path.Combine(dir, "user-phrases.json"));
        Words = new UserWordStatsStore(Path.Combine(dir, "word-stats.json"));
    }

    public UserPhraseStore Phrases { get; }
    public UserWordStatsStore Words { get; }

    public void Dispose()
    {
        Phrases.Dispose();
        Words.Dispose();
    }
}
