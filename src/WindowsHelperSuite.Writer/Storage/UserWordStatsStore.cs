using System.Text.Json;
using WindowsHelperSuite.Writer.Models;

namespace WindowsHelperSuite.Writer.Storage;

public sealed class UserWordStatsStore : IDisposable
{
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly System.Timers.Timer _saveTimer;
    private List<WordRow> _rows = [];
    private bool _dirty;

    public UserWordStatsStore(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowsHelperSuite",
            "writer-ai",
            "word-stats.json");

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _saveTimer = new System.Timers.Timer(2000) { AutoReset = false };
        _saveTimer.Elapsed += (_, _) => FlushSave();
        Load();
    }

    public IReadOnlyList<WordRow> Snapshot()
    {
        lock (_sync)
        {
            return _rows.ToList();
        }
    }

    public void RecordAcceptedWord(string word, WriterContextSnapshot context)
    {
        var w = NormalizeWord(word);
        if (w.Length <= 1)
        {
            return;
        }

        lock (_sync)
        {
            var row = _rows.Find(x => string.Equals(x.Word, w, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new WordRow { Word = w, AcceptCount = 0, LastUtc = DateTime.UtcNow };
                _rows.Add(row);
            }

            row.AcceptCount++;
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

                var doc = JsonSerializer.Deserialize<WordFile>(json, _json);
                _rows = doc?.Words ?? [];
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
                var file = new WordFile { Words = _rows };
                File.WriteAllText(_path, JsonSerializer.Serialize(file, _json));
                _dirty = false;
            }
            catch
            {
                // non-fatal
            }
        }
    }

    private static string NormalizeWord(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var chars = input
            .Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '\'' || ch == '-')
            .ToArray();

        return new string(chars).ToLowerInvariant();
    }

    public sealed class WordRow
    {
        public string Word { get; set; } = "";
        public int AcceptCount { get; set; }
        public DateTime LastUtc { get; set; }
        public string LastTypingMode { get; set; } = "";
        public string LastProcess { get; set; } = "";
    }

    private sealed class WordFile
    {
        public List<WordRow> Words { get; set; } = [];
    }
}
