# Voice Bridge contracts (WindowsHelperSuite)

The Android **Voice Bridge** app should live in a **separate repository**. This folder is the **source of truth** for wire message shapes consumed by `WindowsHelperSuite.VoiceBridge`.

- Serialize JSON with **camelCase** property names to match the Windows host (`VoiceBridgeEnvelope`).
- `type` must be one of the string constants in `VoiceBridgeMessageTypes`.
- **V1 pairing**: connect to `ws://<pc-host>:<port>/ws?token=<sharedToken>` (token also appears in `settings.json` under `voiceBridge.sharedToken` once the listener has started once).

When you add fields, extend `VoiceBridgeEnvelope` and keep Android deserializers tolerant of unknown keys.
