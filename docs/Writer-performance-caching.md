# Writer — focus snapshot caching

## Problem

Per-keystroke work included **UI Automation** (`SecretFieldDetector`) and repeated **foreground process + title** reads (`ForegroundContext`). That adds latency and CPU on fast typing.

## Approach

Cache snapshots while **focus identity** is unchanged:

- **Foreground window HWND** (`GetForegroundWindow`)
- **Keyboard focus HWND** (`GetGUIThreadInfo` → `hwndFocus`)

If both match the last call and elapsed time is within a short TTL, return the **previous** snapshot. Otherwise recompute.

## Defaults

| Wrapper | TTL | Rationale |
|---------|-----|-----------|
| `CachingSecretFieldDetector` | 120 ms | Security-sensitive; refresh quickly if focus moves |
| `CachingWriterContext` | 200 ms | Ranking context; slightly looser |

## Wiring

`ApplicationService` uses:

- `new CachingSecretFieldDetector(new SecretFieldDetector())` for `InputService`
- `new CachingWriterContext(new ForegroundWriterContext())` when no custom `IWriterContext` is injected

## Bypass

Pass a custom `IWriterContext` or `ISecretFieldDetector` in tests; or wrap the **inner** detector only if you need uncached behavior at the outer layer.

## Code

- `Infrastructure/Hooks/FocusIdentity.cs`
- `Infrastructure/Services/CachingSecretFieldDetector.cs`
- `App/Services/Writer/CachingWriterContext.cs`
