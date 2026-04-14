using System.Text.Json;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Storage;

public sealed class UserPhraseStore : IDisposable
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly System.Timers.Timer _saveTimer;
    private List<PhraseRow> _rows = [];
    private bool _dirty;

    public UserPhraseStore(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "writer-ai",
            "user-phrases.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _saveTimer = new System.Timers.Timer(2000) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => FlushSave();
        Load();
    }

    public IReadOnlyList<PhraseRow> Snapshot()
    {
        lock (_sync)
        {
            return _rows.ToList();
        }
    }

    public void BumpPhrase(string phrase, WriterContextSnapshot context)
    {
        var p = NormalizePhrase(phrase);
        if (p.Length < 3 || !p.Contains(' ', StringComparison.Ordinal))
        {
            return;
        }

        lock (_sync)
        {
            var row = _rows.Find(x => string.Equals(x.Text, p, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new PhraseRow { Text = p, Count = 0, LastUtc = DateTime.UtcNow };
                _rows.Add(row);
            }

            row.Count++;
            row.LastUtc = DateTime.UtcNow;
            row.LastTypingMode = context.TypingMode.ToString();
            row.LastProcess = context.ProcessName;
            ScheduleSave();
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        FlushSave();
        _saveTimer.Dispose();
    }

    private void Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _rows = [];
                    return;
                }

                var json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _rows = [];
                    return;
                }

                var doc = JsonSerializer.Deserialize<PhraseFile>(json, _json);
                _rows = doc?.Phrases ?? [];
            }
            catch
            {
                _rows = [];
            }
        }
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        if (!_dirty)
        {
            return;
        }

        lock (_sync)
        {
            try
            {
                var file = new PhraseFile { Phrases = _rows };
                File.WriteAllText(_path, JsonSerializer.Serialize(file, _json));
                _dirty = false;
            }
            catch
            {
                // non-fatal
            }
        }
    }

    private static string NormalizePhrase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return string.Join(' ', input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public sealed class PhraseRow
    {
        public string Text { get; set; } = "";
        public int Count { get; set; }
        public DateTime LastUtc { get; set; }
        public string LastTypingMode { get; set; } = "";
        public string LastProcess { get; set; } = "";
    }

    private sealed class PhraseFile
    {
        public List<PhraseRow> Phrases { get; set; } = [];
    }
}
