# Writer — `ITypingModel` persistence

## Storage

- **Path:** `%AppData%\WindowsHelperSuite\writer-model.json`
- **Format:** JSON (`words`, `phrases`, `corrections`)
- **Save:** debounced ~7s after changes; **Save** + **Dispose** on app shutdown

## Recording (ApplicationService)

| Event | Action |
|--------|--------|
| Word completed (`OnWordTyped`) | `RecordWord` + existing wordbank learn |
| Sentence completed (`OnSentenceTyped`) | `RecordPhrase` (2–10 words, not numeric-only) |
| Suggestion picked (`LearnAcceptedSuggestion`) | `RecordCorrection(typedPartial, firstWord)` when single-word completion replaced typed text |

All recording is skipped when `InputService.IsInProtectedField` is true (password/secret fields).

## Ranking (PredictionService)

- Injects `ITypingModel` and merges personal **corrections**, **words**, and **phrases** into the candidate list.
- Adds `GetRankingBoost` (frequency log + recency + context bucket) on top of base scores after chat/email phrase multipliers.

## Limits

- Words ~15k, phrases ~4k, corrections ~1k — lowest-frequency / oldest trimmed when over cap.

## Tests

Use manual flows from the product guide (phrase repetition, correction after restart, context in Chrome vs Outlook).
