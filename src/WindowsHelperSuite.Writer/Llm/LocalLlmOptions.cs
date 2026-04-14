namespace WindowsHelperSuite.Writer.Llm;

public sealed class LocalLlmOptions
{
    /// <summary>OpenAI-compatible API root (LM Studio: include <c>/v1</c>, e.g. <c>http://localhost:1234/v1</c>).</summary>
    public string BaseUrl { get; set; } = "http://localhost:1234/v1";
    public string Model { get; set; } = "qwen";
    public int TimeoutMs { get; set; } = 700;
    public int MaxSuggestions { get; set; } = 4;
}
