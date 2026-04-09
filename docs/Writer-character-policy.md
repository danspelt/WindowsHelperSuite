# Writer character policy

This document matches the behavior implemented in `InputService` and the normalization used by `PredictionService` (via `WriterWordBufferPolicy` in Core).

## Current word buffer

Characters that **extend** the partial word (and are copied into the sentence buffer as part of that word):

- Unicode letters and decimal digits (`char.IsLetterOrDigit`)
- Apostrophe `'`
- Hyphen-minus `-`

Everything else **does not** extend the current word.

## Session start (“wake”) and caret recovery

A Writer typing session starts (and `TypingStarted` fires) only when the user types a **Unicode letter** with a valid text caret. **Digits, symbols, punctuation, apostrophe, hyphen, and any other non-letter do not wake** the session or restore validated text input after a failed caret check—those keystrokes are ignored by the Writer hook until a letter is typed.

Once a session is active, digits still **extend** the current word like any other word character (e.g. `covid19`), per `IsWordExtendingCharacter` above.

## Word boundaries (keyboard path)

- **Whitespace** (`char.IsWhiteSpace`): completes the current word (if any), clears the partial word, then normalizes spaces in the sentence buffer (`AppendSentenceSeparatorLocked`).
- **Other non-word characters** (punctuation, symbols): completes the current word, appends the character to the **sentence** buffer only, then may end the sentence if the character is `.` `!` or `?` (`IsSentenceTerminator`), which clears the sentence buffer after `SentenceTyped`.
- **Enter**: completes word/sentence, clears buffers, ends typing session, dismisses overlay.
- **Tab / newline in pasted text**: treated like whitespace for boundary purposes (`\n`, `\t` → space-style handling; `\r` skipped).

## Backspace

- Trims the partial word first; if empty, trims the sentence buffer so context stays aligned.

## Predictor alignment

`WriterWordBufferPolicy.NormalizeWord` keeps only word-extending characters, trims, and lowercases. Prefix matching and learning use this so the model sees the same token the buffer conceptually represents.

## Intentional gaps

- **Underscore `_`** is not part of the word in Writer buffers (identifiers in code editors use `_`, but Writer treats it as punctuation). Development mode already suppresses suggestions.
- **Non-ASCII hyphen variants** (en dash, etc.) are not word-extending unless classified as letters by `IsLetterOrDigit` (they are not).

## Tests

See `WriterWordBufferPolicyTests` in `WindowsHelperSuite.Tests` for the shared policy API.
