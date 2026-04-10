import { useEffect } from 'react'
import { useHeadsetAudio } from '../../hooks/useHeadsetAudio'
import { useSettingsStore } from '../../stores/settingsStore'

export function AudioSettingsSection() {
  const headsetOnlyMode = useSettingsStore((s) => s.headsetOnlyMode)
  const setHeadsetOnlyMode = useSettingsStore((s) => s.setHeadsetOnlyMode)
  const preferredOutputDeviceId = useSettingsStore((s) => s.preferredOutputDeviceId)
  const setPreferredOutputDeviceId = useSettingsStore((s) => s.setPreferredOutputDeviceId)
  const preferredInputDeviceId = useSettingsStore((s) => s.preferredInputDeviceId)
  const setPreferredInputDeviceId = useSettingsStore((s) => s.setPreferredInputDeviceId)

  const { ready, canRouteOutput, headsetOutput, headsetConnected, inputs, outputs, refresh } = useHeadsetAudio()

  useEffect(() => {
    void refresh()
  }, [preferredOutputDeviceId, refresh])

  return (
    <fieldset className="audio-fieldset">
      <legend>Audio — OpenRun Pro</legend>
      <p className="muted small">
        When <strong>Use OpenRun Pro only</strong> is on, counselor voice plays only through your Shokz headset (never
        laptop speakers). Live listening stays paused if the headset disconnects.
      </p>
      <label className="check">
        <input
          type="checkbox"
          checked={headsetOnlyMode}
          onChange={(e) => setHeadsetOnlyMode(e.target.checked)}
        />
        Use OpenRun Pro only (private voice mode)
      </label>
      {!canRouteOutput ? (
        <p className="audio-warn">
          This build cannot set a per-app speaker (<code>setSinkId</code> missing). Headset-only routing may not work —
          leave this off or use a newer Electron/Chromium.
        </p>
      ) : null}
      <div className="audio-status">
        <div>
          <span className="audio-label">Output (counselor voice)</span>
          <strong>{headsetOutput?.label ?? '—'}</strong>
        </div>
        <div>
          <span className="audio-label">Status</span>
          <strong className={headsetConnected ? 'ok' : 'bad'}>
            {!ready ? 'Checking…' : headsetConnected ? 'Connected' : 'Disconnected'}
          </strong>
        </div>
      </div>
      <label>
        Bind output device (if auto-detect misses your headset)
        <select
          value={preferredOutputDeviceId}
          onChange={(e) => setPreferredOutputDeviceId(e.target.value)}
        >
          <option value="">Auto: match “OpenRun Pro” / Shokz name</option>
          {outputs.map((d) => (
            <option key={d.deviceId} value={d.deviceId}>
              {d.label || d.deviceId || 'Output'}
            </option>
          ))}
        </select>
      </label>
      <label>
        Input microphone (speech-to-text)
        <select
          value={preferredInputDeviceId}
          onChange={(e) => setPreferredInputDeviceId(e.target.value)}
        >
          <option value="">System default</option>
          {inputs.map((d) => (
            <option key={d.deviceId} value={d.deviceId}>
              {d.label || d.deviceId || 'Microphone'}
            </option>
          ))}
        </select>
      </label>
      <button type="button" className="secondary-btn" onClick={() => void refresh()}>
        Refresh devices
      </button>
    </fieldset>
  )
}
