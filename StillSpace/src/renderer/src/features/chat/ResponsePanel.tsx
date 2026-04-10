type Props = {
  text: string
  busy: boolean
  /** When true, do not offer TTS (text-only session or headset private mode without device). */
  readAloudDisabled?: boolean
  onReplayTts: () => void
}

export function ResponsePanel({ text, busy, readAloudDisabled, onReplayTts }: Props) {
  return (
    <section className="panel">
      <header className="panel__head">
        <h2>Counselor</h2>
        <button
          type="button"
          className="linkish"
          onClick={onReplayTts}
          disabled={!text || busy || readAloudDisabled}
        >
          Read aloud
        </button>
      </header>
      <div className="panel__body">
        {busy ? <p className="muted">Thinking…</p> : null}
        {text ? <p className="reply">{text}</p> : !busy ? <p className="muted">Replies appear here.</p> : null}
      </div>
    </section>
  )
}
