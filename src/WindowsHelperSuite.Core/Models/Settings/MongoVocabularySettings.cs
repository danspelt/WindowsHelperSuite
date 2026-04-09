namespace WindowsHelperSuite.Core.Models.Settings;

/// <summary>Optional sync of learned writer words/phrases to MongoDB (sentence context + counts).</summary>
public class MongoVocabularySettings
{
    /// <summary>When true and <see cref="ConnectionString"/> is set, upserts run after local word-bank learn.</summary>
    public bool EnableSync { get; set; }

    /// <summary>MongoDB connection string (e.g. mongodb://localhost:27017 or Atlas URI).</summary>
    public string ConnectionString { get; set; } = "";

    public string DatabaseName { get; set; } = "WindowsHelperSuite";

    public string CollectionName { get; set; } = "writer_vocabulary";
}
