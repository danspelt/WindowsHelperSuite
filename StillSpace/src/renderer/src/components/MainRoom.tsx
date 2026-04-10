import { Canvas } from '@react-three/fiber'
import { Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { CounselorScene } from '../features/avatar/CounselorScene'
import { ResponsePanel } from '../features/chat/ResponsePanel'
import { CorrectionModal } from '../features/correction/CorrectionModal'
import { CrisisBanner } from '../features/safety/CrisisBanner'
import { MicButton } from '../features/speech/MicButton'
import { TranscriptPanel } from '../features/speech/TranscriptPanel'
import { useHeadsetAudio } from '../hooks/useHeadsetAudio'
import { ensureMicPermission } from '../services/audio/audioDevices'
import { applyGlossaryToTranscript } from '../services/memory/correctionMemory'
import { sendCounselorMessage } from '../services/llm/sendCounselorMessage'
import { detectCrisis } from '../services/safety/crisisDetection'
import { WebSpeechStt } from '../services/stt/webSpeechStt'
import { playOpenAiTts, speakWithBrowserTts, stopAllCounselorAudio } from '../services/tts/playTts'
import { useSessionStore } from '../stores/sessionStore'
import { useSettingsStore } from '../stores/settingsStore'

type Props = {
  onOpenSettings: () => void
  onEndSession: () => void
}

export function MainRoom({ onOpenSettings, onEndSession }: Props) {
  const mode = useSessionStore((s) => s.mode)
  const textOnlySession = useSessionStore((s) => s.textOnlySession)
  const setTextOnlySession = useSessionStore((s) => s.setTextOnlySession)
  const draftUserText = useSessionStore((s) => s.draftUserText)
  const liveTranscript = useSessionStore((s) => s.liveTranscript)
  const listening = useSessionStore((s) => s.listening)
  const history = useSessionStore((s) => s.history)
  const lastAssistant = useSessionStore((s) => s.lastAssistant)
  const setDraftUserText = useSessionStore((s) => s.setDraftUserText)
  const setLiveTranscript = useSessionStore((s) => s.setLiveTranscript)
  const setListening = useSessionStore((s) => s.setListening)
  const appendUser = useSessionStore((s) => s.appendUser)
  const setAssistant = useSessionStore((s) => s.setAssistant)

  const sttLang = useSettingsStore((s) => s.sttLang)
  const preferredName = useSettingsStore((s) => s.preferredName)
  const autoReadAloud = useSettingsStore((s) => s.autoReadAloud)
  const preferOpenAiTts = useSettingsStore((s) => s.preferOpenAiTts)
  const pauseBeforeReplyMs = useSettingsStore((s) => s.pauseBeforeReplyMs)
  const headsetOnlyMode = useSettingsStore((s) => s.headsetOnlyMode)
  const preferredInputDeviceId = useSettingsStore((s) => s.preferredInputDeviceId)

  const { ready, canRouteOutput, headsetOutput, headsetConnected, refresh } = useHeadsetAudio()

  const sttRef = useRef(new WebSpeechStt())
  const [busy, setBusy] = useState(false)
  const [crisis, setCrisis] = useState(false)
  const [correctionOpen, setCorrectionOpen] = useState(false)
  const [speaking, setSpeaking] = useState(false)
  const spaceHeld = useRef(false)
  const prevHeadsetConnected = useRef<boolean | null>(null)

  /** Mic / STT: only tied to headset *presence* in OpenRun-only mode — not to TTS routing (`setSinkId`). */
  const voiceLiveOk = !textOnlySession && (!headsetOnlyMode || headsetConnected)

  const voiceOutOk =
    !textOnlySession &&
    (!headsetOnlyMode || (headsetConnected && canRouteOutput && Boolean(headsetOutput?.deviceId)))

  const stopListen = useCallback(() => {
    sttRef.current.stop()
    setListening(false)
  }, [setListening])

  useEffect(() => {
    if (!headsetOnlyMode || textOnlySession || !ready) {
      prevHeadsetConnected.current = headsetConnected
      return
    }
    if (prevHeadsetConnected.current === true && headsetConnected === false) {
      stopAllCounselorAudio()
      stopListen()
      setSpeaking(false)
    }
    prevHeadsetConnected.current = headsetConnected
  }, [ready, headsetOnlyMode, headsetConnected, textOnlySession, stopListen])

  useEffect(() => {
    void ensureMicPermission()
  }, [])

  const startListen = useCallback(
    async (opts?: { keepAlive?: boolean }) => {
      if (textOnlySession || busy) return
      if (headsetOnlyMode && !headsetConnected) return
      if (!sttRef.current.isAvailable()) {
        setLiveTranscript('Speech recognition unavailable — use typing.')
        return
      }
      setListening(true)
      setLiveTranscript('')
      await sttRef.current.prepareInput(preferredInputDeviceId.trim() || undefined)
      sttRef.current.start(
        sttLang,
        {
          onPartial: (t) => setLiveTranscript(t),
          onFinal: (t) => {
            const merged = applyGlossaryToTranscript(t)
            const d = useSessionStore.getState().draftUserText
            setDraftUserText(d ? `${d} ${merged}`.trim() : merged)
          },
          onError: () => stopListen()
        },
        { keepAlive: opts?.keepAlive ?? false }
      )
    },
    [
      textOnlySession,
      busy,
      headsetOnlyMode,
      headsetConnected,
      setLiveTranscript,
      preferredInputDeviceId,
      sttLang,
      setDraftUserText,
      stopListen,
      setListening
    ]
  )

  useEffect(() => {
    return () => sttRef.current.stop()
  }, [])

  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (e.code !== 'Space') return
      const t = e.target as HTMLElement
      if (t.tagName === 'TEXTAREA' || t.tagName === 'INPUT' || t.isContentEditable) return
      e.preventDefault()
      if (spaceHeld.current || !voiceLiveOk) return
      spaceHeld.current = true
      void startListen({ keepAlive: false })
    }
    function onKeyUp(e: KeyboardEvent) {
      if (e.code !== 'Space') return
      const t = e.target as HTMLElement
      if (t.tagName === 'TEXTAREA' || t.tagName === 'INPUT' || t.isContentEditable) return
      e.preventDefault()
      spaceHeld.current = false
      stopListen()
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('keyup', onKeyUp)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('keyup', onKeyUp)
    }
  }, [startListen, stopListen, voiceLiveOk])

  async function playReply(text: string) {
    if (!text.trim() || textOnlySession) return
    setSpeaking(true)
    try {
      if (headsetOnlyMode) {
        const sid = headsetOutput?.deviceId
        if (!sid || !canRouteOutput) return
        await playOpenAiTts(text, { sinkId: sid, blockOnSinkFailure: true })
        return
      }
      if (preferOpenAiTts) {
        const ok = await playOpenAiTts(text)
        if (ok) return
      }
      speakWithBrowserTts(text)
    } finally {
      setSpeaking(false)
    }
  }

  async function onSend() {
    const raw = draftUserText.trim() || liveTranscript.trim()
    const text = applyGlossaryToTranscript(raw).trim()
    if (!text || busy) return
    setCrisis(detectCrisis(text) === 'elevated')
    const userPayload = preferredName.trim()
      ? `The user’s preferred name is “${preferredName.trim()}”. Their message:\n\n${text}`
      : text
    setBusy(true)
    try {
      if (pauseBeforeReplyMs > 0) await new Promise((r) => setTimeout(r, pauseBeforeReplyMs))
      const res = await sendCounselorMessage(mode, history, userPayload)
      if (!res.ok) {
        setAssistant(`(Could not reach the AI service.) ${res.error}`)
        return
      }
      appendUser(text)
      setAssistant(res.text)
      if (autoReadAloud && voiceOutOk) void playReply(res.text)
    } finally {
      setBusy(false)
    }
  }

  function toggleMicButton() {
    if (listening) stopListen()
    else void startListen({ keepAlive: true })
  }

  const mistakenBasis = draftUserText.trim() || liveTranscript.trim()

  const showHeadsetDisconnectBanner = headsetOnlyMode && !textOnlySession && ready && !headsetConnected
  const showHeadsetRoutingWarn =
    headsetOnlyMode && !textOnlySession && ready && headsetConnected && !canRouteOutput

  const readAloudBlocked = textOnlySession || (headsetOnlyMode && (!headsetConnected || !canRouteOutput))

  return (
    <div className="main">
      <CrisisBanner visible={crisis} />
      {showHeadsetDisconnectBanner ? (
        <div className="headset-banner" role="status">
          <p>
            <strong>OpenRun Pro not connected.</strong> Live voice input and counselor audio are paused. We will not
            switch replies to your laptop speakers.
          </p>
          <div className="headset-banner__actions">
            <button type="button" className="primary" onClick={() => void refresh()}>
              Retry
            </button>
            <button
              type="button"
              onClick={() => {
                stopAllCounselorAudio()
                stopListen()
                setTextOnlySession(true)
              }}
            >
              Use text only
            </button>
          </div>
        </div>
      ) : null}
      {showHeadsetRoutingWarn ? (
        <div className="headset-warn-strip" role="status">
          Headset is connected, but this build cannot route counselor voice to a specific speaker (
          <code>setSinkId</code> unavailable). You can still use the <strong>live mic</strong>; replies stay text-only
          unless you turn off OpenRun-only or update the app.
        </div>
      ) : null}
      <header className="topbar">
        <span className="brand">Still Space</span>
        <div className="topbar__actions">
          <button type="button" className="linkish" onClick={onOpenSettings}>
            Settings
          </button>
          <button
            type="button"
            onClick={() => {
              stopListen()
              onEndSession()
            }}
          >
            End session
          </button>
        </div>
      </header>
      {textOnlySession ? (
        <p className="session-strip muted">Text-only session — voice input and read-aloud are off for privacy.</p>
      ) : null}
      <div className="main__grid">
        <div className="viewport">
          <Canvas shadows camera={{ position: [0, 1.45, 2.6], fov: 45 }}>
            <Suspense fallback={null}>
              <CounselorScene listening={listening} speaking={speaking} />
            </Suspense>
          </Canvas>
        </div>
        <aside className="sidebar">
          <TranscriptPanel
            live={liveTranscript}
            draft={draftUserText}
            onDraftChange={setDraftUserText}
            onOpenCorrection={() => setCorrectionOpen(true)}
          />
          <div className="controls">
            <MicButton
              listening={listening}
              disabled={busy || !voiceLiveOk}
              onToggle={toggleMicButton}
            />
            <button type="button" className="primary" disabled={busy} onClick={() => void onSend()}>
              Send to counselor
            </button>
          </div>
          <ResponsePanel
            text={lastAssistant}
            busy={busy}
            readAloudDisabled={readAloudBlocked}
            onReplayTts={() => void playReply(lastAssistant)}
          />
        </aside>
      </div>
      {correctionOpen ? (
        <CorrectionModal mistaken={mistakenBasis} onClose={() => setCorrectionOpen(false)} />
      ) : null}
    </div>
  )
}
