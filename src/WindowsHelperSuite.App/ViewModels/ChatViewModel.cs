using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsHelperSuite.AI.Contracts;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly IConversationStore _store;
    private readonly ILoggingService _log;
    private readonly ChatOptions _options;

    private CancellationTokenSource? _streamCts;
    private Action? _openSettingsAction;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
    public ObservableCollection<ConversationSummary> RecentChats { get; } = new();

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private string _chatTitle = "New Chat";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private ConversationSummary? _selectedChat;

    public bool HasMessages => Messages.Count > 0;
    public System.Windows.Visibility EmptyStateVisibility =>
        Messages.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility MessageListVisibility =>
        Messages.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    private ChatConversation _conversation = new();

    public ChatViewModel(IChatService chatService, IConversationStore store,
        ChatOptions options, ILoggingService log)
    {
        _chatService = chatService;
        _store = store;
        _options = options;
        _log = log;
        Messages.CollectionChanged += (_, _) => NotifyVisibilityChanged();
    }

    public void SetOpenSettingsAction(Action action) => _openSettingsAction = action;

    private void NotifyVisibilityChanged()
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(MessageListVisibility));
    }

    public async Task InitializeAsync()
    {
        await LoadRecentChatsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ErrorMessage = null;
        InputText = "";

        var userMsg = new ChatMessage { Role = "user", Content = text };
        _conversation.Messages.Add(userMsg);
        Messages.Add(new ChatMessageViewModel(userMsg));

        // Auto-title from first user message
        if (_conversation.Messages.Count(m => m.Role == "user") == 1)
        {
            _conversation.Title = text.Length > 50 ? text[..50] + "…" : text;
            ChatTitle = _conversation.Title;
        }

        var assistantVm = new ChatMessageViewModel("assistant") { IsStreaming = true };
        Messages.Add(assistantVm);

        IsBusy = true;
        IsStreaming = true;
        StatusText = "Thinking…";
        SendCommand.NotifyCanExecuteChanged();

        try
        {
            var request = BuildChatRequest();

            if (_options.UseStreaming)
            {
                _streamCts = new CancellationTokenSource();
                bool firstChunk = true;
                await foreach (var chunk in _chatService.StreamAsync(request, _streamCts.Token))
                {
                    if (chunk.IsCompleted) break;
                    if (firstChunk) { StatusText = "Streaming reply…"; firstChunk = false; }
                    assistantVm.AppendContent(chunk.TextDelta);
                }
            }
            else
            {
                var response = await _chatService.SendAsync(request);
                if (!response.Success)
                {
                    ErrorMessage = response.ErrorMessage;
                    assistantVm.Content = $"[Error] {response.ErrorMessage}";
                }
                else
                {
                    assistantVm.Content = response.Content;
                }
            }

            assistantVm.IsStreaming = false;

            var assistantMsg = assistantVm.ToModel();
            _conversation.Messages.Add(assistantMsg);
            await _store.SaveAsync(_conversation);
            await RefreshRecentEntry();
        }
        catch (OperationCanceledException)
        {
            _log.Information("[ChatVM] Stream cancelled by user");
            assistantVm.IsStreaming = false;
            StatusText = "Cancelled";
            if (string.IsNullOrEmpty(assistantVm.Content))
            {
                assistantVm.Content = "[Cancelled]";
            }

            var partialMsg = assistantVm.ToModel();
            _conversation.Messages.Add(partialMsg);
            await _store.SaveAsync(_conversation);
        }
        catch (Exception ex)
        {
            _log.Error($"[ChatVM] Send failed: {ex.Message}", ex);
            ErrorMessage = ex.Message;
            assistantVm.Content = $"[Error] {ex.Message}";
            assistantVm.IsStreaming = false;
        }
        finally
        {
            IsBusy = false;
            IsStreaming = false;
            if (ErrorMessage != null)
                StatusText = "Error";
            else if (StatusText != "Cancelled")
                StatusText = "Ready";
            _streamCts?.Dispose();
            _streamCts = null;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(InputText);

    private ChatRequest BuildChatRequest()
    {
        var history = _conversation.Messages
            .Where(m =>
                (m.Role == "user" || m.Role == "assistant") &&
                !string.IsNullOrWhiteSpace(m.Content))
            .ToList();

        return new ChatRequest
        {
            Model = _options.Model,
            SystemPrompt = _options.DefaultSystemPrompt,
            Messages = history,
            Temperature = _options.Temperature,
        };
    }

    [RelayCommand]
    private void CancelGeneration()
    {
        _streamCts?.Cancel();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        _openSettingsAction?.Invoke();
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        _conversation = new ChatConversation();
        Messages.Clear();
        ChatTitle = "New Chat";
        InputText = "";
        ErrorMessage = null;
        StatusText = "Ready";
        IsBusy = false;
        IsStreaming = false;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ClearChatAsync()
    {
        if (_conversation.Messages.Count == 0) return;
        _conversation.Messages.Clear();
        Messages.Clear();
        _conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await _store.SaveAsync(_conversation);
    }

    [RelayCommand]
    private async Task LoadConversationAsync(ConversationSummary? summary)
    {
        if (summary == null) return;
        var conv = await _store.LoadAsync(summary.Id);
        if (conv == null) return;

        _conversation = conv;
        ChatTitle = conv.Title;
        Messages.Clear();
        foreach (var m in conv.Messages.Where(m => m.Role != "system"))
        {
            Messages.Add(new ChatMessageViewModel(m));
        }

        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeleteConversationAsync(ConversationSummary? summary)
    {
        if (summary == null) return;
        await _store.DeleteAsync(summary.Id);
        RecentChats.Remove(summary);

        if (_conversation.Id == summary.Id)
        {
            await NewChatAsync();
        }
    }

    [RelayCommand]
    private void CopyLastResponse()
    {
        var last = Messages.LastOrDefault(m => m.IsAssistant);
        if (last != null && !string.IsNullOrEmpty(last.Content))
        {
            try
            {
                System.Windows.Clipboard.SetText(last.Content);
            }
            catch (Exception ex)
            {
                _log.Warning($"[ChatVM] Copy failed: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void RetryLast()
    {
        if (_conversation.Messages.Count < 2) return;

        // Remove last assistant message
        var lastAssistant = _conversation.Messages.LastOrDefault(m => m.Role == "assistant");
        if (lastAssistant != null) _conversation.Messages.Remove(lastAssistant);

        var lastVm = Messages.LastOrDefault(m => m.IsAssistant);
        if (lastVm != null) Messages.Remove(lastVm);

        // Re-send the last user message
        var lastUser = _conversation.Messages.LastOrDefault(m => m.Role == "user");
        if (lastUser != null)
        {
            _conversation.Messages.Remove(lastUser);
            var userVm = Messages.LastOrDefault(m => m.IsUser);
            if (userVm != null) Messages.Remove(userVm);

            InputText = lastUser.Content;
        }
    }

    private async Task LoadRecentChatsAsync()
    {
        RecentChats.Clear();
        var recent = await _store.GetRecentAsync(30);
        foreach (var c in recent)
        {
            RecentChats.Add(new ConversationSummary
            {
                Id = c.Id,
                Title = c.Title,
                UpdatedAt = c.UpdatedAt,
                MessageCount = c.Messages.Count,
            });
        }
    }

    private async Task RefreshRecentEntry()
    {
        var existing = RecentChats.FirstOrDefault(c => c.Id == _conversation.Id);
        if (existing != null) RecentChats.Remove(existing);

        RecentChats.Insert(0, new ConversationSummary
        {
            Id = _conversation.Id,
            Title = _conversation.Title,
            UpdatedAt = _conversation.UpdatedAt,
            MessageCount = _conversation.Messages.Count,
        });

        await Task.CompletedTask;
    }

    partial void OnInputTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }
}

public class ConversationSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public int MessageCount { get; set; }
    public string TimeAgo => FormatTimeAgo(UpdatedAt);

    private static string FormatTimeAgo(DateTimeOffset dt)
    {
        var span = DateTimeOffset.Now - dt;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return dt.LocalDateTime.ToString("MMM d");
    }
}
