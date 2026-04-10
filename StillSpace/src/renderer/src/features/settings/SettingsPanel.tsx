import type { CounselorMode } from '../../prompts/counselorPrompts'
import { useSettingsStore } from '../../stores/settingsStore'
import { useSessionStore } from '../../stores/sessionStore'
import { AudioSettingsSection } from './AudioSettingsSection'

const modes: { id: CounselorMode; label: string }[] = [
  { id: 'support', label: 'Support' },
  { id: 'reflection', label: 'Reflection' },
  { id: 'grounding', label: 'Grounding' },
  { id: 'practical', label: 'Practical' },
  { id: 'quiet', label: 'Quiet' }
]

export function SettingsPanel({ onClose }: { onClose: () => void }) {
  const mode = useSessionStore((s) => s.mode)
  const setMode = useSessionStore((s) => s.setMode)
  const preferredName = useSettingsStore((s) => s.preferredName)
  const setPreferredName = useSettingsStore((s) => s.setPreferredName)
  const sttLang = useSettingsStore((s) => s.sttLang)
  const setSttLang = useSettingsStore((s) => s.setSttLang)
  const autoReadAloud = useSettingsStore((s) => s.autoReadAloud)
  const setAutoReadAloud = useSettingsStore((s) => s.setAutoReadAloud)
  const preferOpenAiTts = useSettingsStore((s) => s.preferOpenAiTts)
  const setPreferOpenAiTts = useSettingsStore((s) => s.setPreferOpenAiTts)
  const pauseBeforeReplyMs = useSettingsStore((s) => s.pauseBeforeReplyMs)
  const setPauseBeforeReplyMs = useSettingsStore((s) => s.setPauseBeforeReplyMs)

  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-label="Settings">
      <div className="modal">
        <header className="modal__head">
          <h2>Settings</h2>
          <button type="button" className="linkish" onClick={onClose}>
            Close
          </button>
        </header>
        <div className="modal__body">
          <label>
            Preferred name (optional)
            <input
              value={preferredName}
              onChange={(e) => setPreferredName(e.target.value)}
              placeholder="How you’d like to be addressed"
            />
          </label>
          <label>
            Speech language
            <input value={sttLang} onChange={(e) => setSttLang(e.target.value)} placeholder="en-US" />
          </label>
          <label className="check">
            <input type="checkbox" checked={autoReadAloud} onChange={(e) => setAutoReadAloud(e.target.checked)} />
            Auto read replies aloud
          </label>
          <label className="check">
            <input
              type="checkbox"
              checked={preferOpenAiTts}
              onChange={(e) => setPreferOpenAiTts(e.target.checked)}
            />
            Prefer OpenAI TTS when API key is set (else Windows / browser voice — disabled in OpenRun-only mode)
          </label>
          <AudioSettingsSection />
          <label>
            Pause before sending to AI (ms)
            <input
              type="number"
              min={0}
              max={5000}
              step={100}
              value={pauseBeforeReplyMs}
              onChange={(e) => setPauseBeforeReplyMs(Number(e.target.value))}
            />
          </label>
          <fieldset>
            <legend>Counselor mode</legend>
            <div className="mode-grid">
              {modes.map((m) => (
                <label key={m.id} className="check">
                  <input type="radio" name="mode" checked={mode === m.id} onChange={() => setMode(m.id)} />
                  {m.label}
                </label>
              ))}
            </div>
          </fieldset>
          <p className="muted small">
            Memory: session-only for this MVP. Corrections are stored locally in this app data to improve transcripts.
          </p>
        </div>
      </div>
    </div>
  )
}
