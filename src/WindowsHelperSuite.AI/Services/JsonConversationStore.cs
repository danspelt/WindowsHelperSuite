using System.Text.Json;
using WindowsHelperSuite.AI.Contracts;
using WindowsHelperSuite.AI.Models;
using WindowsHelperSuite.Core.Interfaces;

namespace WindowsHelperSuite.AI.Services;

public class JsonConversationStore : IConversationStore
{
    private readonly string _directory;
    private readonly ILoggingService _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JsonConversationStore(ILoggingService log, string? directory = null)
    {
        _log = log;
        _directory = directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowsHelperSuite", "Chats");
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(ChatConversation conversation)
    {
        try
        {
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            var path = GetPath(conversation.Id);
            var json = JsonSerializer.Serialize(conversation, JsonOpts);
            await File.WriteAllTextAsync(path, json);
            _log.Debug($"[ConversationStore] Saved {conversation.Id}");
        }
        catch (Exception ex)
        {
            _log.Error($"[ConversationStore] Save failed: {ex.Message}", ex);
        }
    }

    public async Task<ChatConversation?> LoadAsync(string conversationId)
    {
        try
        {
            var path = GetPath(conversationId);
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<ChatConversation>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _log.Error($"[ConversationStore] Load failed: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<ChatConversation>> GetRecentAsync(int maxCount = 50)
    {
        var results = new List<ChatConversation>();
        try
        {
            if (!Directory.Exists(_directory)) return results;

            var files = Directory.GetFiles(_directory, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(maxCount);

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var conv = JsonSerializer.Deserialize<ChatConversation>(json, JsonOpts);
                    if (conv != null) results.Add(conv);
                }
                catch (Exception ex)
                {
                    _log.Warning($"[ConversationStore] Skipping corrupt file {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[ConversationStore] GetRecent failed: {ex.Message}", ex);
        }

        return results.OrderByDescending(c => c.UpdatedAt).ToList();
    }

    public Task DeleteAsync(string conversationId)
    {
        try
        {
            var path = GetPath(conversationId);
            if (File.Exists(path))
            {
                File.Delete(path);
                _log.Debug($"[ConversationStore] Deleted {conversationId}");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[ConversationStore] Delete failed: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    private string GetPath(string id) => Path.Combine(_directory, $"{id}.json");
}
