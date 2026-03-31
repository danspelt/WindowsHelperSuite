# Writer — password / secret field detection

## Implemented behavior

- **Pipeline order:** every keystroke → `ISecretFieldDetector.GetSnapshot()` → if protected, **return immediately** from the hook handler (no buffer updates, no `TextCaptured` / `WordTyped` / `SentenceTyped`).
- **UI:** `SecretFieldProtectionChanged(true)` → `OnSecretFieldProtectionChanged` stops speech, hides overlay, clears suggestions and phrase context.
- **Learning:** No events fire from protected typing, so prediction / phrases / typo store are not updated.
- **Manual actions:** `AddToWordBank` / `AddPhraseToWordBank` / `ShowOverlay` no-op when `InputService.IsInProtectedField`.

## Detection layers

| Layer | Source |
|--------|--------|
| UIA | `AutomationElement.IsPasswordProperty` |
| Heuristic | Name, AutomationId, ClassName, HelpText — keywords (password, token, otp, mfa, pin word-boundary, etc.) — **metadata only** |
| Browser | Process is a known browser + window title suggests auth (`sign in`, `login`, `authentication`, …) — **fail-safe** |

## Logging

Debug logs may include **Reason**, **Process**, **ControlType** — never typed characters or field values.

## QA checklist

Use the matrix in the product spec (browsers, desktop auth, PIN/OTP, normal fields beside secrets). Verify: overlay off in protected fields; no learning after typing a password; fresh state after leaving the field.

## Follow-ups (Phase 2+)

- App-specific overrides (Outlook, Discord, Electron) where UIA is unreliable.
- Optional throttling/caching of `GetSnapshot()` if profiling shows cost.
- `ITypingModel` persistence guide (next after this safety work).
