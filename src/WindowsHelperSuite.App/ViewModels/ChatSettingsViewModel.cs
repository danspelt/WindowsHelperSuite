using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsHelperSuite.AI.Contracts;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.App.ViewModels;

public partial class ChatSettingsViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ChatOptions _options;
    private readonly ILoggingService _log;
    private readonly Action? _onSaved;

    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private bool _useStreaming = true;
    [ObservableProperty] private double _temperature = 0.7;
    [ObservableProperty] private int _timeoutSeconds = 120;
    [ObservableProperty] private string _defaultSystemPrompt = "";
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _testResult = "";
    [ObservableProperty] private bool _testSuccess;

    public ChatSettingsViewModel(IChatService chatService, ChatOptions options,
        ILoggingService log, Action? onSaved = null)
    {
        _chatService = chatService;
        _options = options;
        _log = log;
        _onSaved = onSaved;

        LoadFromOptions();
    }

    private void LoadFromOptions()
    {
        BaseUrl = _options.BaseUrl;
        ApiKey = _options.ApiKey;
        Model = _options.Model;
        UseStreaming = _options.UseStreaming;
        Temperature = _options.Temperature;
        TimeoutSeconds = _options.TimeoutSeconds;
        DefaultSystemPrompt = _options.DefaultSystemPrompt;
    }

    [RelayCommand]
    private void Save()
    {
        _options.BaseUrl = BaseUrl.Trim();
        _options.ApiKey = ApiKey.Trim();
        _options.Model = Model.Trim();
        _options.UseStreaming = UseStreaming;
        _options.Temperature = Temperature;
        _options.TimeoutSeconds = TimeoutSeconds;
        _options.DefaultSystemPrompt = DefaultSystemPrompt;

        _log.Information($"[ChatSettings] Saved → host={SafeHost(BaseUrl)}, model={Model}");
        _onSaved?.Invoke();
    }

    [RelayCommand]
    private void Reset()
    {
        var defaults = new ChatOptions();
        BaseUrl = defaults.BaseUrl;
        ApiKey = "";
        Model = defaults.Model;
        UseStreaming = defaults.UseStreaming;
        Temperature = defaults.Temperature;
        TimeoutSeconds = defaults.TimeoutSeconds;
        DefaultSystemPrompt = defaults.DefaultSystemPrompt;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestConnectionAsync(CancellationToken ct)
    {
        IsTesting = true;
        TestResult = "Testing connection…";
        TestSuccess = false;

        // Apply current UI values temporarily for the test
        var prevBaseUrl = _options.BaseUrl;
        var prevApiKey = _options.ApiKey;
        var prevModel = _options.Model;

        try
        {
            _options.BaseUrl = BaseUrl.Trim();
            _options.ApiKey = ApiKey.Trim();
            _options.Model = Model.Trim();

            var response = await _chatService.TestConnectionAsync(ct);
            if (response.Success)
            {
                TestResult = $"Connected successfully. Model replied: \"{Truncate(response.Content, 80)}\"";
                TestSuccess = true;
            }
            else
            {
                TestResult = $"Connection failed: {response.ErrorMessage}";
                TestSuccess = false;
            }
        }
        catch (OperationCanceledException)
        {
            TestResult = "Test cancelled.";
        }
        catch (Exception ex)
        {
            TestResult = $"Test failed: {ex.Message}";
            _log.Warning($"[ChatSettings] Test connection failed: {ex.Message}");
        }
        finally
        {
            // Restore previous options if user hasn't saved
            _options.BaseUrl = prevBaseUrl;
            _options.ApiKey = prevApiKey;
            _options.Model = prevModel;
            IsTesting = false;
        }
    }

    private static string SafeHost(string url)
    {
        try { return new Uri(url).Host; }
        catch { return "(invalid)"; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
