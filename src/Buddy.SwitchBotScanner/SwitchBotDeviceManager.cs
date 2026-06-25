using System.Text.Json;

namespace Buddy.SwitchBotScanner;

public class SwitchBotDeviceManager : ISwitchBotDeviceManager
{
    private readonly SwitchBotScanner _scanner;
    private readonly string _preferredPath;

    public SwitchBotDeviceManager(SwitchBotScanner scanner, string preferredPath = @"C:\ProgramData\Buddy\switchbot-preferred.json")
    {
        _scanner = scanner;
        _preferredPath = preferredPath;
    }

    public async Task<BleCandidate?> OpenDoorAsync()
    {
        // 1. Try the last successful SwitchBot first.
        var preferred = await GetPreferredAsync();
        if (preferred != null && await _scanner.TryConnectToSwitchBotAsync(preferred))
        {
            preferred.LastSeen = DateTime.UtcNow;
            await SavePreferredAsync(preferred);
            return preferred;
        }

        // 2. If it failed, scan BLE and try each candidate.
        var found = await _scanner.FindWorkingSwitchBotAsync();
        if (found == null)
        {
            return null;
        }

        // 3. Save it as the new preferred device and update the candidate list.
        await SetPreferredAsync(found);
        await MergeWithSavedCandidatesAsync(found);
        return found;
    }

    public async Task<BleCandidate?> GetPreferredAsync()
    {
        if (!File.Exists(_preferredPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_preferredPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<BleCandidate>(json);
    }

    public async Task SetPreferredAsync(BleCandidate candidate)
    {
        if (candidate == null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        await SavePreferredAsync(candidate);
    }

    private async Task SavePreferredAsync(BleCandidate candidate)
    {
        var directory = Path.GetDirectoryName(_preferredPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(candidate, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_preferredPath, json);
    }

    private async Task MergeWithSavedCandidatesAsync(BleCandidate preferred)
    {
        var all = await _scanner.LoadCandidatesAsync();
        var existing = all.FirstOrDefault(c => c.BluetoothAddress == preferred.BluetoothAddress);
        if (existing != null)
        {
            all.Remove(existing);
        }

        all.Insert(0, preferred);
        await _scanner.SaveCandidatesAsync(all);
    }
}
