# Windows Assistant Worker — V1 Technical Spec

Companion to [Windows-Assistant-Worker-Developer-Guide.md](./Windows-Assistant-Worker-Developer-Guide.md). This is the **concrete V1** contract: namespaces/classes (logical), IPC payloads, state machine, and the **first ten commands**.

## 1. V1 goals

Deliver a **local-only** C# worker that:

- Accepts **text** commands from a host (named pipe recommended for V1).  
- Parses intents with a **small rule-based** parser (regex + alias tables); LLM optional later behind the same `AssistantCommand` boundary.  
- Builds a **linear plan** of steps; executes **safe** steps immediately; **stops and requests confirmation** before any step marked risky.  
- Uses **UI Automation** first; Win32 for window/process tasks; **no OCR** in V1 unless explicitly added as Phase 2.  

## 2. Class / interface list (V1)

Suggested project: `Assistant.Worker` (.NET 8 Windows).

| Type | Responsibility |
|------|------------------|
| `WorkerHost` | Owns pipe server lifetime, dispatches incoming envelopes, publishes outbound events. |
| `IAssistantSession` | Per-connection or per-user session: correlation id, pending confirmation handle. |
| `CommandEnvelope` | DTO: `MessageType`, `CorrelationId`, `Payload` (JSON or strongly typed). |
| `CommandIntakeService` | Validates envelope → `AssistantCommand`. |
| `RuleBasedIntentParser` | `string` → `AssistantCommand` (intent + parameters). |
| `IPlanner` / `SimplePlanner` | `AssistantCommand` → `ActionPlan`. |
| `SafetyService` | Marks steps `IsRisky`, sets `Plan.RequiresConfirmation`, validates allowed intents. |
| `AutomationExecutor` | Runs `ActionStep` sequence until confirmation barrier or completion. |
| `WindowManager` | Foreground window, enumerate, focus, restore. |
| `ProcessLauncher` | Resolve alias → `ProcessStartInfo`, start or reuse. |
| `UiTreeReader` | Walk focused window / root, find `AutomationElement` by Name/AutomationId/ControlType. |
| `InputController` | `SendKeys`/UIA `ValuePattern`/invoke patterns; scroll. |
| `ScreenReaderService` | Compose short summary from `UiTreeReader` + active window metadata. |
| `AppAliasRegistry` | Load JSON aliases for app resolution. |
| `WorkerLogger` | Structured logs + redaction hook. |
| `ConfirmationToken` | Opaque id referencing pending risky operation (host sends `assistant_confirm` / `assistant_cancel`). |

**Models** (as in the guide): `AssistantCommand`, `ActionPlan`, `ActionStep`, plus:

```csharp
public sealed class AssistantResult
{
    public string CorrelationId { get; init; } = "";
    public string State { get; init; } = ""; // completed | awaiting_confirmation | failed
    public string UserMessage { get; init; } = "";
    public IReadOnlyList<string> StepLog { get; init; } = Array.Empty<string>();
    public string? ConfirmationPrompt { get; init; }
    public string? PendingToken { get; init; }
}
```

## 3. IPC transport (V1)

**Recommended:** single **named pipe** per machine, e.g. `\\.\pipe\WindowsHelperSuite.Assistant.v1`.

- **Framing:** newline-delimited JSON (NDJSON) or length-prefixed UTF-8 JSON (pick one; NDJSON is simplest for prototypes).  
- **Security:** restrict to same user / same session; optional ACL.  
- **Host:** WindowsHelperSuite tray app, Electron shell, or a future dedicated UI — any process that can open the pipe client.  

## 4. Message schema (host ↔ worker)

### 4.1 Host → worker

**`assistant_command`** — run a command from text.

```json
{
  "type": "assistant_command",
  "correlationId": "8f3c…",
  "payload": {
    "rawText": "Open Chrome",
    "source": "voice"
  }
}
```

**`assistant_confirm`** — user approved pending risky step.

```json
{
  "type": "assistant_confirm",
  "correlationId": "8f3c…",
  "payload": {
    "token": "pend_…"
  }
}
```

**`assistant_cancel`** — abort pending plan or confirmation.

```json
{
  "type": "assistant_cancel",
  "correlationId": "8f3c…",
  "payload": {}
}
```

**`assistant_ping`** — health check.

```json
{
  "type": "assistant_ping",
  "correlationId": "…",
  "payload": {}
}
```

### 4.2 Worker → host

**`assistant_status`** — progress.

```json
{
  "type": "assistant_status",
  "correlationId": "8f3c…",
  "payload": {
    "phase": "executing",
    "message": "Focusing Google Chrome"
  }
}
```

**`assistant_result`** — terminal or awaiting confirmation.

```json
{
  "type": "assistant_result",
  "correlationId": "8f3c…",
  "payload": {
    "state": "awaiting_confirmation",
    "userMessage": "I'm ready to send this message. Send it now?",
    "confirmationToken": "pend_…",
    "stepLog": ["Focused Messenger", "Typed draft in message box"]
  }
}
```

**`assistant_error`** — hard failure.

```json
{
  "type": "assistant_error",
  "correlationId": "8f3c…",
  "payload": {
    "code": "control_not_found",
    "message": "I couldn't find a control named Send."
  }
}
```

**`assistant_pong`**

```json
{
  "type": "assistant_pong",
  "correlationId": "…",
  "payload": { "ok": true }
}
```

## 5. State machine (worker session)

States:

| State | Description |
|-------|-------------|
| **Idle** | Waiting for input. |
| **Parsing** | Raw text → intent. |
| **Planning** | Intent → plan; safety classification. |
| **Executing** | Running auto-safe steps. |
| **AwaitingConfirmation** | Stopped before risky step; `PendingToken` issued. |
| **Completed** | Plan finished successfully. |
| **Failed** | Unrecoverable error; user message returned. |
| **Cancelled** | Host sent cancel or session reset. |

**Transitions**

- Idle → Parsing on `assistant_command`.  
- Parsing → Planning on success; → Failed on parse error.  
- Planning → Executing; → Failed if plan empty/invalid.  
- Executing → AwaitingConfirmation when next step `IsRisky`; → Completed when no steps left; → Failed on exception.  
- AwaitingConfirmation → Executing on `assistant_confirm` with valid token; → Cancelled on `assistant_cancel`.  

## 6. First ten commands (V1 implement in this order)

Each maps to an **intent** string in `AssistantCommand.Intent`. Parameters live in `Parameters` (string dictionary).

| # | Intent | Parameters | Default safety | Notes |
|---|--------|------------|----------------|-------|
| 1 | `open_app` | `app` (alias key) | Safe | Launch or focus if already running. |
| 2 | `focus_app` | `app` or `titleContains` | Safe | Focus best-match window. |
| 3 | `get_active_window` | — | Safe | Return foreground title, process name, bounds summary. |
| 4 | `read_screen_summary` | — | Safe | UIA-based summary of foreground window. |
| 5 | `list_windows` | optional `filter` | Safe | Top-level windows for disambiguation (limit N). |
| 6 | `click_named_control` | `name` or `automationId` | Safe | Prefer InvokePattern; ambiguous → result asks user to pick. |
| 7 | `focus_control` | `name` or `automationId` | Safe | Set focus to edit or container. |
| 8 | `type_text` | `text` | Safe *if* focused element is text-editable; else **fail with message** | Never implies send/submit. |
| 9 | `scroll` | `direction` = `up|down`, optional `amount` | Safe | Scroll focused control or foreground scrollable ancestor. |
| 10 | `copy_selected_text` | — | Safe | Prefer UIA `TextPattern` selection; if unavailable, return clear “could not read selection”. |

**Intentionally not in the first ten (but stub in `SafetyService`):**

- `send_message`, `submit_form`, `delete_file`, `close_window_unsaved` → always **Confirm** or **Blocked** until confirmation UX exists end-to-end.  

## 7. Risky step examples (post–first ten)

When you add messaging workflows:

- `press_send` / `submit` → `IsRisky: true`, `RequiresConfirmation: true`, prompt per guide §20.  

## 8. Testing (V1)

- **Unit:** `RuleBasedIntentParser`, `SafetyService`, alias resolution.  
- **Integration:** run worker on a VM/snapshot; tests for `open_app` (notepad), `get_active_window`, `read_screen_summary` against known windows.  
- **No** production credentials or real social accounts in automated tests.  

## 9. Alignment with this repo

WindowsHelperSuite today is a **.NET** tray/helper app. A logical path is:

- Add `src/WindowsHelperSuite.AssistantWorker` (class library + optional console host for pipes), then reference from `WindowsHelperSuite.App` when you wire the tray UI to **Assistant mode**.  
- Keep **StillSpace** (counselor) and **Assistant worker** processes separate until you define a single host.  

This spec is stable enough to start **Phase 1** in the guide: pipe host + `open_app` + `focus_app` + `get_active_window` + status events.
