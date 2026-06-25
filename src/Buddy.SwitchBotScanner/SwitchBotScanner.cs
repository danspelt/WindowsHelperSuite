using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Buddy.SwitchBotScanner;

public class SwitchBotScanner
{
    // SwitchBot BLE service UUID used by most SwitchBot devices (Bot, Meter, Curtain, etc.).
    private static readonly Guid SwitchBotServiceUuid = new("CBA20D00-224D-11E6-9FB8-0002A5D5C51B");

    private readonly string _storagePath;
    private readonly ConcurrentDictionary<ulong, BleCandidate> _discovered = new();

    public SwitchBotScanner(string storagePath = @"C:\ProgramData\Buddy\switchbot-devices.json")
    {
        _storagePath = storagePath;
    }

    public Task<List<BleCandidate>> ScanAsync(int seconds)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "Scan duration must be positive.");
        }

        return ScanAsyncImpl(seconds);
    }

    private async Task<List<BleCandidate>> ScanAsyncImpl(int seconds)
    {
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        watcher.Received += OnAdvertisementReceived;
        watcher.Start();

        await Task.Delay(TimeSpan.FromSeconds(seconds));

        watcher.Stop();
        watcher.Received -= OnAdvertisementReceived;

        return _discovered.Values
            .OrderByDescending(d => d.LooksLikeSwitchBot)
            .ThenByDescending(d => d.Rssi)
            .ToList();
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var name = args.Advertisement.LocalName ?? string.Empty;
        var manufacturerData = FormatManufacturerData(args.Advertisement.ManufacturerData);
        var serviceUuids = args.Advertisement.ServiceUuids.Select(u => u.ToString()).ToList();

        var candidate = new BleCandidate
        {
            BluetoothAddress = args.BluetoothAddress,
            Name = name,
            Rssi = args.RawSignalStrengthInDBm,
            LastSeen = DateTime.UtcNow,
            ManufacturerData = manufacturerData,
            ServiceUuids = serviceUuids
        };

        _discovered.AddOrUpdate(args.BluetoothAddress, candidate, (_, old) =>
        {
            old.Name = string.IsNullOrWhiteSpace(name) ? old.Name : name;
            old.Rssi = args.RawSignalStrengthInDBm;
            old.LastSeen = DateTime.UtcNow;
            old.ManufacturerData = manufacturerData ?? old.ManufacturerData;
            old.ServiceUuids = serviceUuids.Count > 0 ? serviceUuids : old.ServiceUuids;
            return old;
        });
    }

    private static string? FormatManufacturerData(IList<BluetoothLEManufacturerData> data)
    {
        if (data == null || data.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var item in data)
        {
            var bytes = item.Data.ToArray();
            sb.Append(BitConverter.ToString(bytes).Replace("-", string.Empty));
            sb.Append(';');
        }

        return sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : null;
    }

    public async Task<BleCandidate?> FindWorkingSwitchBotAsync()
    {
        var scanned = await ScanAsync(15);
        var saved = await LoadCandidatesAsync();
        var merged = MergeCandidates(scanned, saved);

        foreach (var candidate in merged)
        {
            if (await TryConnectToSwitchBotAsync(candidate))
            {
                candidate.LastSeen = DateTime.UtcNow;
                return candidate;
            }
        }

        return null;
    }

    private static List<BleCandidate> MergeCandidates(List<BleCandidate> scanned, List<BleCandidate> saved)
    {
        var merged = new Dictionary<ulong, BleCandidate>();

        foreach (var candidate in scanned
            .OrderByDescending(c => c.LooksLikeSwitchBot)
            .ThenByDescending(c => c.Rssi))
        {
            merged[candidate.BluetoothAddress] = candidate;
        }

        foreach (var candidate in saved
            .OrderByDescending(c => c.LooksLikeSwitchBot)
            .ThenByDescending(c => c.Rssi))
        {
            if (!merged.ContainsKey(candidate.BluetoothAddress))
            {
                merged[candidate.BluetoothAddress] = candidate;
            }
        }

        return merged.Values
            .OrderByDescending(c => c.LooksLikeSwitchBot)
            .ThenByDescending(c => c.Rssi)
            .ToList();
    }

    public async Task<bool> TryConnectToSwitchBotAsync(BleCandidate candidate)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(candidate.BluetoothAddress);
            if (device == null)
            {
                return false;
            }

            candidate.DeviceId = device.DeviceId;

            var switchBotServices = await device.GetGattServicesForUuidAsync(SwitchBotServiceUuid);
            if (switchBotServices.Status != GattCommunicationStatus.Success || switchBotServices.Services.Count == 0)
            {
                return false;
            }

            candidate.LooksLikeSwitchBot = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveCandidatesAsync(List<BleCandidate> devices)
    {
        if (devices == null)
        {
            throw new ArgumentNullException(nameof(devices));
        }

        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_storagePath, json);
    }

    public async Task<List<BleCandidate>> LoadCandidatesAsync()
    {
        if (!File.Exists(_storagePath))
        {
            return new List<BleCandidate>();
        }

        var json = await File.ReadAllTextAsync(_storagePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<BleCandidate>();
        }

        var result = JsonSerializer.Deserialize<List<BleCandidate>>(json);
        return result ?? new List<BleCandidate>();
    }
}
