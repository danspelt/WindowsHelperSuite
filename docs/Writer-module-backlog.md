# Writer module — implementation backlog

Ordered for risk reduction and dependency flow. Check items off as you ship.

## Phase 1 — Safety and input fidelity

1. **Password / sensitive fields** — Done: `ISecretFieldDetector` + `SecretFieldDetector` (UIA `IsPassword`, metadata heuristics, conservative browser auth-title rule). `InputService` exits before buffer updates when protected; `ApplicationService` hides overlay, stops speech, clears suggestion/learning state. See `docs/Writer-password-field-detection.md`.
2. **Character policy** — Done: `WriterWordBufferPolicy` (Core) shared by `InputService` + `PredictionService`; `docs/Writer-character-policy.md`; `WriterWordBufferPolicyTests`.
3. **Hook QA** — Fast typing, hold-to-repeat, app switch mid-word; verify ordering and no duplicate `TextCaptured` for the same physical key.

## Phase 2 — Context engine

4. **Foreground process** — Done: `ForegroundContext` + `ForegroundWriterContext` map process name → `WriterTypingMode` (`Infrastructure/Hooks/ForegroundContext.cs`).
5. **Optional window title hints** — `WriterContextSnapshot.ForegroundWindowTitle` is filled; use for future heuristics (privacy-sensitive).
6. **Plumb context into ranking** — Done: `GetSuggestions(..., WriterContextSnapshot)`; phrase boosts in chat/email; **development** mode returns no suggestions (code editors).

## Phase 3 — Adaptive typing

7. **Persist typing model** — Done: `TypingModelService` → `writer-model.json` (words/phrases/corrections, context counts, debounced save, caps). Wired to `RecordWord` / `RecordPhrase` / `RecordCorrection`; `PredictionService` merges + `GetRankingBoost`.
8. **Correction map** — Wordbank + optional `corrections.json` + personal layer in typing model from explicit picks (`RecordCorrection`).
9. **Phrase prefix index** — Wordbank `PhrasePrefixBucket` + personal phrases in typing model (`Prefix` = first word).

## Phase 4 — Overlay and speech polish

10. **Placement** — Cursor-near positioning, avoid covering caret; smooth show/hide.
11. **Headset-only speech** — Already gated by settings; verify paths when device changes at runtime.

## Phase 5 — Automation

12. **Regression checklist** — Automate or script the character-set and boundary tests from the product spec.

## Phase 6 — Performance

13. **Focus snapshot caching** — Done: `FocusIdentity` + `CachingSecretFieldDetector` (120 ms) + `CachingWriterContext` (200 ms). See `docs/Writer-performance-caching.md`.

---

*Schema reference:* `%AppData%\WindowsHelperSuite\data\wordbank.json` (words, phrases, bigrams, optional `Corrections` and `PhrasePrefixes`); optional `%AppData%\WindowsHelperSuite\data\corrections.json` merged at load. See `docs/corrections.example.json` and `docs/wordbank-adaptive-fields.example.json` in the repo.
