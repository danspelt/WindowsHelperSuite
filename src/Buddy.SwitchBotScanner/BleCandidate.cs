using System.Text.Json.Serialization;

namespace Buddy.SwitchBotScanner;

public class BleCandidate
{
    public string Name { get; set; } = string.Empty;

    public ulong BluetoothAddress { get; set; }

    [JsonIgnore]
    public string AddressHex => BluetoothAddress.ToString("X12");

    public short Rssi { get; set; }

    public DateTime LastSeen { get; set; }

    public bool LooksLikeSwitchBot { get; set; }

    public string? DeviceId { get; set; }

    public string? ManufacturerData { get; set; }

    public List<string> ServiceUuids { get; set; } = new();
}
