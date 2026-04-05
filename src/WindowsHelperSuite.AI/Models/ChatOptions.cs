namespace WindowsHelperSuite.AI.Models;

public class ChatOptions
{
    public string ProviderName { get; set; } = "OpenAI-Compatible";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public bool UseStreaming { get; set; } = true;
    public double Temperature { get; set; } = 0.7;
    public int TimeoutSeconds { get; set; } = 120;
    public bool EnableSpeech { get; set; } = false;
    public string DefaultSystemPrompt { get; set; } =
        "You are a helpful, clear, and concise assistant. " +
        "Keep replies short unless the user asks for detail. " +
        "Produce accessible, easy-to-read writing. " +
        "Be supportive and friendly.";
}
