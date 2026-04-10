# Windows Assistant Worker — Developer Guide

This document describes the **task-focused Windows automation worker**: desktop control, screen reading, and safe execution. It is **not** the emotional counselor. Keep the two workers isolated by product mode, permissions, and UX.

## 1. Purpose

The Windows Assistant worker helps the user **operate the computer** through voice and text commands.

**In scope**

- Open and focus apps; switch windows  
- Read what is on screen (UI Automation first)  
- Type dictated text into focused fields (draft-only until explicit send flow exists)  
- Click UI elements, scroll, basic navigation  
- Assist with repetitive workflows  
- **Confirm** risky actions before executing them  

**Qualities**

- Accessible, predictable, private, safe, transparent  

## 2. Product role

The assistant should feel like a **trusted operator**: practical, clear, and never reckless. The counselor can be warm; the assistant should be **capable and explicit**.

Example utterances:

- “Open Chrome.” / “Go to Facebook.”  
- “Read this screen.” / “Read selected text.”  
- “Click the message box.” / “Scroll down.”  
- “Switch to Messenger.” / “Type what I say.”  

**Loop**

1. Intake (heard or typed command)  
2. Interpret intent  
3. Plan steps  
4. Guardrails (risk class)  
5. Execute safe steps; pause for confirmation on risky steps  
6. Report outcome (success, failure, or question)  

## 3. Architectural principle

| Worker | Role |
|--------|------|
| **Counselor** | Emotional support, reflection, grounding |
| **Windows Assistant** | Task execution, desktop control, automation |

**Why split**

- Clear mental model  
- Safer permission boundaries  
- Appropriate tone per surface  
- Easier debugging  

## 4. Recommended stack

- **.NET 8**, **C#**  
- **UI Automation** (`System.Windows.Automation`) for structure and control discovery  
- **Win32** for window enumeration, focus, launch, geometry, lower-level input when needed  
- **OCR last** — only when UIA is insufficient  
- **IPC to host**: named pipes, localhost HTTP, or WebSocket (pick one for V1 and keep it local-only)  
- **Speech**: host or bridge handles STT; worker receives **text** (keeps worker testable)  

**UI options**

- **A.** Electron/React host + C# worker (matches a 3D/counselor shell)  
- **B.** Fully native WPF/WinUI + automation in-process  

## 5. Core responsibilities (domains)

- **App control** — launch, focus, switch, restore minimized  
- **UI navigation** — find by name/automation id, click, focus, menus, scroll  
- **Text** — type into focused control, copy/paste/replace where safe  
- **Screen understanding** — title, visible controls, short summary of purpose  
- **Workflows** — guided sequences (e.g. open Messenger → focus composer → type draft → **stop before send**)  
- **Safety** — classify risk; confirm before send/delete/purchase/irreversible actions  

## 6. Non-goals (V1)

- Full autonomous browser agent  
- Hidden purchases or account changes  
- Unrestricted deletion  
- OCR-first automation by default  
- Counseling behavior inside this worker  
- Stealth or invisible operation  

## 7. UX rules

The user should always see:

- What was heard (transcript)  
- What will happen (plan summary)  
- Current step  
- Confirmation prompts when required  
- Clear failures  

Never: silent risky actions, hidden errors, or unexpected audio routing (align with **headset-only** policy in the host if applicable).  

## 8. Safety model (three classes)

| Class | Behavior | Examples |
|-------|-----------|----------|
| **Safe — auto** | Execute immediately | Open app, focus window, scroll, read screen, focus text field, type **draft** in focused field, copy selection, read title |
| **Confirm** | User must approve | Send message, submit form, delete/move files, close unsaved, purchases, sensitive settings |
| **Blocked (V1)** | Not supported | Covert surveillance, irreversible system damage without flow, auto-exfiltration of secrets |

## 9. System architecture (logical components)

1. **Command intake** — voice/text/hotkey (host may own voice; worker receives normalized text)  
2. **Intent parser** — raw text → structured intent  
3. **Planner** — intent → ordered steps  
4. **Automation executor** — UIA + Win32 + input injection  
5. **Guardrail layer** — risk, confirmation, context checks  
6. **Feedback layer** — status events back to host  

## 10. Suggested repository layout

```txt
/windows-assistant-worker
  /src
    /Assistant.Worker
      /Commands
      /Planning
      /Execution
      /Safety
      /Windows
      /Speech        # optional: bridges only
      /Screen
      /Models
      /Services
      /Interop
  /tests
    /Assistant.Worker.Tests
  /docs
```

## 11. Command and planning models (sketch)

Normalized command:

```csharp
public sealed class AssistantCommand
{
    public string RawText { get; init; } = "";
    public string Intent { get; init; } = "";
    public Dictionary<string, string> Parameters { get; init; } = new();
    public bool RequiresConfirmation { get; init; }
    public string Source { get; init; } = "text"; // voice | text | hotkey
}
```

Plan:

```csharp
public sealed class ActionPlan
{
    public string Goal { get; init; } = "";
    public List<ActionStep> Steps { get; init; } = new();
    public bool RequiresConfirmation { get; init; }
}

public sealed class ActionStep
{
    public string Id { get; init; } = "";
    public string ActionType { get; init; } = "";
    public Dictionary<string, string> Parameters { get; init; } = new();
    public bool IsRisky { get; init; }
}
```

## 12. Windows integration services (implementations)

| Service | Responsibility |
|---------|------------------|
| **WindowManager** | Enumerate windows, active window, focus, restore, title/process match |
| **ProcessLauncher** | Start by alias, verify success, known-app registry |
| **UiTreeReader** | UIA tree walk, find by name/automation id, list visible controls |
| **InputController** | Type text, shortcuts, click control, scroll |
| **ScreenReaderService** | Summarize foreground context, selected text (UIA → clipboard policy) |
| **SafetyService** | Classify steps, gate execution, confirmation tokens |

## 13. App aliases

Map natural language to executables / app user model IDs, e.g.:

```json
{
  "chrome": ["chrome", "google chrome", "browser"],
  "messenger": ["messenger", "facebook messenger"],
  "explorer": ["file explorer", "explorer", "folders"],
  "settings": ["settings", "windows settings"]
}
```

## 14. Execution rules

1. **Verify state** before typing or clicking (e.g. text field focused, or ask which field).  
2. **Typing ≠ sending** — never send on “type” alone.  
3. **Disambiguate** when multiple targets match.  
4. **Report failures** in plain language.  

## 15. Live voice mode (host concern)

Continuous listen + VAD + transcript can live in the **host**. The worker should accept **text commands** and emit **structured status** so the host can enforce **Counselor vs Assistant** mode and headset routing.  

## 16. Headset / audio

Worker TTS (if any) should follow the same **private output** rules as the rest of the product: route to the user’s headset sink when in strict mode; **no speaker fallback**; on disconnect, pause voice feedback and surface status (host may own audio device selection).  

## 17. Screen-reading contract

**“Read this screen”** → active app, window title, major visible controls, one-sentence purpose guess.  
**“Read selected text”** → UIA selection patterns first; clipboard only with explicit user consent policy.  

## 18. Logging (privacy-aware)

Log: timestamps, intent, plan ids, step types, outcomes, confirmation events.  
Avoid: passwords, message bodies, secrets — use **redaction** and a configurable privacy mode.  

## 19. Security principles

Least privilege, visible activity, explicit initiation, confirmations for high-risk intents, no silent fallback to unsafe paths.  

## 20. Accessibility

Support typed commands, large status, transcript of what was heard, retries, short confirmations; optional later: switch scanning, custom hotkeys.  

---

**Next document:** [Windows-Assistant-Worker-V1-Spec.md](./Windows-Assistant-Worker-V1-Spec.md) — class list, IPC message schema, state machine, first ten commands.
