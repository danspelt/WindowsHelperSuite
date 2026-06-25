# Buddy.SwitchBotScanner

Windows BLE scanner module for the Windows Buddy app. It scans nearby Bluetooth Low Energy devices, detects likely SwitchBot devices, stores their addresses/IDs, and tries each saved address until one responds.

## Usage

```csharp
using Buddy.SwitchBotScanner;

var scanner = new SwitchBotScanner();

// Scan for 15 seconds and list candidates.
var candidates = await scanner.ScanAsync(15);

// Save discovered candidates.
await scanner.SaveCandidatesAsync(candidates);

// Load previously saved candidates.
var saved = await scanner.LoadCandidatesAsync();

// Find the first working SwitchBot (scan + try saved + try scanned).
var working = await scanner.FindWorkingSwitchBotAsync();

// High-level door flow: try preferred, scan, save preferred, fail gracefully.
var manager = new SwitchBotDeviceManager(scanner);
var device = await manager.OpenDoorAsync();
if (device == null)
{
    Console.WriteLine("I cannot find the SwitchBot.");
}
```

## Required API surface

```csharp
Task<List<BleCandidate>> ScanAsync(int seconds);
Task<BleCandidate?> FindWorkingSwitchBotAsync();
Task<bool> TryConnectToSwitchBotAsync(BleCandidate candidate);
Task SaveCandidatesAsync(List<BleCandidate> devices);
Task<List<BleCandidate>> LoadCandidatesAsync();
```

## Detection logic

- Scans in active mode for a configurable duration.
- **Does not rely on device names.** SwitchBot devices often advertise with no name or a private/random address.
- Sorts results by signal strength (RSSI).
- A device is confirmed as a SwitchBot only when `TryConnectToSwitchBotAsync` successfully discovers the SwitchBot service UUID: `CBA20D00-224D-11E6-9FB8-0002A5D5C51B`.
- `FindWorkingSwitchBotAsync` scans and then tests every discovered device until one exposes that service.

## Storage

Default storage paths:

- `C:\ProgramData\Buddy\switchbot-devices.json` — all discovered/known candidates.
- `C:\ProgramData\Buddy\switchbot-preferred.json` — the last successfully used SwitchBot.

You can override these paths in the constructors.

## Important Windows capability

The consuming app must declare the `bluetooth` capability in its application manifest (`Package.appxmanifest`):

```xml
<Capabilities>
  <Capability Name="internetClient" />
  <DeviceCapability Name="bluetooth" />
</Capabilities>
```

For unpackaged desktop apps, make sure the process has Bluetooth permissions and the user has enabled Bluetooth.

## Address stability warning

BLE addresses on Windows may be random or private and can change over time. This module saves the Windows BLE address/ID as a **candidate**, not a permanent identity. The `OpenDoorAsync` flow always re-scans and re-tests candidates so a changed address can be rediscovered.

## Recommended production path

For reliable "open door" commands from a distance, prefer the SwitchBot Cloud API through a SwitchBot Hub:

```
Windows Buddy → SwitchBot Cloud API → SwitchBot Hub → Door device
```

Use this BLE module as a setup tool or local fallback.
