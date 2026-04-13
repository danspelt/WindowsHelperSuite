using System.Text.Json;
using System.Text.RegularExpressions;

namespace StillSpace.Services;

public sealed class CorrectionMemoryStore
{
    private const int MaxEntries = 200;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    public CorrectionMemoryStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StillSpace");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "corrections.json");
    }

    public IReadOnlyList<CorrectionEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<CorrectionEntry>();
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<CorrectionEntry>>(json);
            return list is { Count: > 0 } ? list : Array.Empty<CorrectionEntry>();
        }
        catch
        {
            return Array.Empty<CorrectionEntry>();
        }
    }

    public CorrectionEntry Save(string mistaken, string corrected, string? context = null)
    {
        var list = Load().ToList();
        var entry = new CorrectionEntry
        {
            Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Guid.NewGuid():N}",
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Mistaken = mistaken.Trim(),
            Corrected = corrected.Trim(),
            Context = string.IsNullOrWhiteSpace(context) ? null : context.Trim()
        };
        list.Insert(0, entry);
        if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
        File.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOpts));
        return entry;
    }

    public IReadOnlyDictionary<string, string> Glossary()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Load())
        {
            var k = e.Mistaken.Trim().ToLowerInvariant();
            if (k.Length > 0 && e.Corrected.Trim().Length > 0) map[k] = e.Corrected.Trim();
        }
        return map;
    }

    public string ApplyGlossary(string text)
    {
        var outText = text;
        foreach (var (wrong, right) in Glossary())
        {
            if (wrong.Length == 0) continue;
            var escaped = Regex.Escape(wrong);
            outText = Regex.Replace(outText, $@"\b{escaped}\b", right, RegexOptions.IgnoreCase);
        }

        return outText;
    }
}
