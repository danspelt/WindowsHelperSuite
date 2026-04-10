import { loadCorrections } from '../services/memory/correctionMemory'
import { useHeadsetAudio } from '../hooks/useHeadsetAudio'
import { useSessionStore } from '../stores/sessionStore'
import { useSettingsStore } from '../stores/settingsStore'

type Props = {
  onOpenSettings: () => void
}

export function WelcomeScreen({ onOpenSettings }: Props) {
  const startSession = useSessionStore((s) => s.startSession)
  const headsetOnlyMode = useSettingsStore((s) => s.headsetOnlyMode)
  const { ready, canRouteOutput, headsetConnected, headsetOutput, refresh } = useHeadsetAudio()
  const n = loadCorrections().length

  /** Voice session can start when headset is present for private mode; TTS routing is separate. */
  const voiceStartOk = !headsetOnlyMode || headsetConnected

  return (
    <div className="welcome">
      <div className="welcome__card">
        <h1>Still Space</h1>
        <p className="lede">
          A calm 3D room, voice and text, and a counselor tone tuned for imperfect speech and emotional safety.
        </p>
        {headsetOnlyMode ? (
          <div className="welcome__audio">
            <p className="muted small">
              <strong>OpenRun Pro only</strong> is on. Counselor audio routes to your Shokz headset — never the laptop
              speakers.
            </p>
            <p className="audio-status-line">
              Output: <strong>{headsetOutput?.label ?? '—'}</strong> · Status:{' '}
              <strong className={headsetConnected ? 'ok' : 'bad'}>
                {!ready ? 'Checking…' : headsetConnected ? 'Connected' : 'Disconnected'}
              </strong>
            </p>
            {!canRouteOutput ? (
              <p className="audio-warn">
                This build may not route counselor speech to a specific speaker. Your <strong>live mic</strong> can still
                work when the headset is connected; replies may be text-only until routing is available.
              </p>
            ) : null}
          </div>
        ) : null}
        <ul className="welcome__list">
          <li>Session memory only (this MVP)</li>
          <li>{n} saved transcript correction{n === 1 ? '' : 's'} on this device</li>
        </ul>
        <div className="welcome__actions">
          <button
            type="button"
            className="primary"
            disabled={!voiceStartOk}
            title={
              !voiceStartOk && headsetOnlyMode
                ? 'Connect your OpenRun Pro (or disable OpenRun-only in Settings).'
                : undefined
            }
            onClick={() => startSession()}
          >
            Start voice session
          </button>
          <button type="button" onClick={() => startSession({ textOnly: true })}>
            Start text-only session
          </button>
          <button type="button" onClick={() => void refresh()}>
            Retry devices
          </button>
          <button type="button" onClick={onOpenSettings}>
            Settings
          </button>
        </div>
        {headsetOnlyMode && !voiceStartOk && ready ? (
          <p className="muted small">
            Headset not ready for private voice. Use <strong>Start text-only session</strong>, connect your OpenRun Pro,
            then tap <strong>Retry devices</strong>.
          </p>
        ) : null}
        <p className="muted small">
          Not a substitute for professional care. Add <code>OPENAI_API_KEY</code> in <code>.env</code> for cloud
          replies. OpenRun-only mode uses OpenAI speech routed to your headset (no browser voice fallback).
        </p>
      </div>
    </div>
  )
}
