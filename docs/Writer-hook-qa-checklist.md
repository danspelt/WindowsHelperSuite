# Writer hook — manual QA checklist

Use this when validating [`InputService`](e:/Git/WindowsHelperSuite/src/WindowsHelperSuite.Input/Services/InputService.cs) behavior after changes to the low-level keyboard hook or buffer logic.

## Environments

- At least one **rich edit** target (Notepad, Word, browser textarea, Outlook body).
- One **password** field (browser login or similar) to confirm protected-field suppression.

## Scenarios

1. **Fast typing** — Type a full sentence quickly; overlay suggestions update without large lag; no duplicated `TextCaptured`/garbled partial word in logs (if logging is enabled).

2. **Key repeat** — Hold a letter; buffer and overlay should stay consistent or recover when repeat stops (no runaway buffer growth).

3. **App switch mid-word** — Type half a word, Alt+Tab to another app, return and continue; writer session rules should match product spec (overlay may hide after focus loss; returning should not leave stale suggestions permanently).

4. **Space and word commit** — Word boundaries, `WordTyped`, and TTS should match visible text after edits involving backspace (see `WriterWordBufferPolicy` tests).

5. **Paste** — Paste a block; buffers and suggestion context should reflect merged text where paste handling is wired.

6. **Protected field** — Focus a password box; overlay hides, speech stops, learning does not run (see `SecretFieldDetector` docs).

Record failures with: app name, control type, steps, and whether live caret text (`Win32Caret`) matched the hook buffer.
