# Mode system (contributor guide)

This document describes how **Writer** vs **Hotkey** mode works in the codebase, where to change behavior, and how settings persist.

## Behavior summary

| Mode | Writer assistance | Volume / system hotkeys |
|------|-------------------|-------------------------|
| **Writer** | Input hook, overlay, suggestions, writer hotkeys enabled | Volume hotkeys disabled |
| **Hotkey** | Writer input off, overlay hidden, writer paths guarded | Volume hotkeys enabled |

The **mode menu** opens from a global gesture (default **`Ctrl+F3`**). The hook attempts to **consume** that chord so it does not reach the focused application when the low-level hook runs in this process.

## Source map (exact locations)

### Core (domain + contracts)

| Piece | Path |
|-------|------|
| `AppMode` enum | `src/WindowsHelperSuite.Core/Modes/AppMode.cs` |
| `ModeDefinition` (static flags per mode) | `src/WindowsHelperSuite.Core/Modes/ModeDefinition.cs` |
| `ModeChangeResult` | `src/WindowsHelperSuite.Core/Modes/ModeChangeResult.cs` |
| `IModeManager` | `src/WindowsHelperSuite.Core/Interfaces/IModeManager.cs` |
| `ModeSystemSettings` | `src/WindowsHelperSuite.Core/Models/Settings/ModeSystemSettings.cs` |
| `AppSettings.ModeSystem` | `src/WindowsHelperSuite.Core/Models/Settings/AppSettings.cs` |

### App (orchestration + UI)

| Piece | Path |
|-------|------|
| `ModeManager` | `src/WindowsHelperSuite.App/Services/ModeManager.cs` |
| Mode menu (WPF) | `src/WindowsHelperSuite.App/ModeMenuWindow.xaml` + `.xaml.cs` |
| Mode toast (WPF) | `src/WindowsHelperSuite.App/ModeToastWindow.xaml` + `.xaml.cs` |
| Apply mode to subsystems | `src/WindowsHelperSuite.App/Services/ApplicationService.cs` (`ApplyApplicationMode`, hotkey registration, guards) |
| Tray tooltip shows mode | `src/WindowsHelperSuite.App/Services/TrayIconService.cs` (`ApplyModeIndicator`) |

### Infrastructure / hotkeys

| Piece | Path |
|-------|------|
| Low-level hook, hotkey matching, optional key consumption | `src/WindowsHelperSuite.Infrastructure/Services/KeyboardHookService.cs` |
| `RegisterHotkey(..., consumeMatchingKeys)` | `src/WindowsHelperSuite.Hotkeys/` (`IHotkeyService` / `HotkeyService`) |
| Settings JSON (enums) | `src/WindowsHelperSuite.Infrastructure/Services/SettingsService.cs` |

Settings load/save (including enum JSON) use **`WindowsHelperSuite.Infrastructure`** / `SettingsService` (`JsonStringEnumConverter` with camelCase for enum names in JSON).

## Settings model (`settings.json`)

Under the root settings object, **`modeSystem`** is serialized from `ModeSystemSettings`:

```json
{
  "modeSystem": {
    "currentMode": "hotkey",
    "menuHotkeyGesture": "Ctrl+F3",
    "showModeToast": true,
    "speakModeChange": false,
    "rememberLastMode": true
  }
}
```

- **`currentMode`**: `writer` or `hotkey` (camelCase in JSON).
- **First-run default** in code: **`Hotkey`** (`ModeSystemSettings.CurrentMode`).
- **`rememberLastMode`**: when true, successful switches update `currentMode` on save.

There is **no Settings UI** for these flags in V1; edit the JSON or add UI later.

## Runtime flow

1. **`ApplicationService`** constructs **`ModeManager`** with a delegate that calls **`ApplyApplicationMode(AppMode)`**.
2. **`ModeManager.Initialize()`** reads persisted mode and applies it once (no celebratory toast on cold start unless you add it).
3. **`RegisterDefaultHotkeys`** registers **`OpenModeMenu`** using `ModeSystem.MenuHotkeyGesture`, with **consumption** enabled so **Ctrl+F3** is less likely to leak to other apps.
4. User opens **`ModeMenuWindow`** (modal on the app dispatcher); choices call **`ModeManager.SwitchMode`** or open settings / cancel.
5. **`SwitchMode`** runs the apply delegate, updates settings on success (save failures are logged; in-memory mode may still apply—see `ModeManager` implementation), fires **`ModeChanged`**, and **`ApplicationService`** shows toast/speech per `ModeSystemSettings`.

## Extending the system

- **New subsystem gated by mode**: Prefer a single place—**`ApplyApplicationMode`** in `ApplicationService`—or subscribe to **`IModeManager.ModeChanged`** from a small service.
- **New `AppMode` value**: Add enum member, extend **`ModeDefinition.For`**, persistence already uses the enum name; update **`ApplyApplicationMode`** and any hotkey `ShouldRun` checks.
- **Menu items**: Edit **`ModeMenuWindow`** (XAML + key handling) and wire actions in **`ApplicationService`** (or a dedicated presenter) so the menu stays thin.

## Testing checklist (manual)

- **Ctrl+F3** opens the menu from another app’s focus.
- Keys **1–4**, **numpad**, **arrows + Enter**, **Esc** behave as specified.
- Mode persists across restart when **`rememberLastMode`** is true.
- Writer features inactive in **Hotkey** mode; volume hotkeys inactive in **Writer** mode.
- Rapid switching does not crash; failed apply paths log and roll back where implemented.

For the full product requirements and UX spec, refer to the internal **Developer Guide — Mode System** document you maintain alongside this repo.
