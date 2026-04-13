namespace StillSpace.Writer;

/// <summary>OpenAI fields used by <see cref="WriterAssistantClient"/> (implemented by app settings).</summary>
public interface IWriterOpenAiSettings
{
    string OpenAiApiKey { get; }
    string OpenAiHintModel { get; }
}
