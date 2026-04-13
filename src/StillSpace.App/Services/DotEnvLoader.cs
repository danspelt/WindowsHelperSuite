namespace StillSpace.Services;

/// <summary>
/// Minimal .env loader (KEY=VALUE lines) compatible with the Electron app’s .env.example.
/// Loads every existing file in order; later files override earlier keys (user AppData wins over exe folder).
/// </summary>
public static class DotEnvLoader
{
    public static void TryLoad(params string[] candidatePaths)
    {
        foreach (var path in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#')) continue;
                    var eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = t[..eq].Trim();
                    var val = t[(eq + 1)..].Trim().Trim('"');
                    if (key.Length == 0) continue;
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
