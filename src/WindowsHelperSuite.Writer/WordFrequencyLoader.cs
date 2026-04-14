using System.Reflection;

namespace WindowsHelperSuite.Writer;

/// <summary>Loads tiered word frequencies from the embedded english-words resource (same format as Prediction EnglishDictionary).</summary>
public static class WordFrequencyLoader
{
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Default = new(LoadDefaultInternal);

    public static IReadOnlyDictionary<string, int> LoadDefault() => Default.Value;

    private static IReadOnlyDictionary<string, int> LoadDefaultInternal()
    {
        const string resourceName = "WindowsHelperSuite.Writer.Resources.english-words.txt";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        using var reader = new StreamReader(stream);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentFreq = 5;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                if (int.TryParse(trimmed.AsSpan(1), out var freq))
                {
                    currentFreq = freq;
                }

                continue;
            }

            map[trimmed] = currentFreq;
        }

        return map;
    }
}
