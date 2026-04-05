using CommunityToolkit.Mvvm.ComponentModel;
using WindowsHelperSuite.AI.Models;

namespace WindowsHelperSuite.App.ViewModels;

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _role = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _timestamp = "";
    [ObservableProperty] private bool _isStreaming;

    public string Id { get; }
    public bool IsUser => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem => Role == "system";

    public ChatMessageViewModel(ChatMessage msg)
    {
        Id = msg.Id;
        Role = msg.Role;
        Content = msg.Content;
        Timestamp = msg.Timestamp.LocalDateTime.ToString("h:mm tt");
    }

    public ChatMessageViewModel(string role)
    {
        Id = Guid.NewGuid().ToString();
        Role = role;
        Content = "";
        Timestamp = DateTimeOffset.Now.LocalDateTime.ToString("h:mm tt");
    }

    public void AppendContent(string text)
    {
        Content += text;
        OnPropertyChanged(nameof(Content));
    }

    public ChatMessage ToModel() => new()
    {
        Id = Id,
        Role = Role,
        Content = Content,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
