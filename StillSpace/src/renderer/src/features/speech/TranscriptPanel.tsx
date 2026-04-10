type Props = {
  live: string
  draft: string
  onDraftChange: (v: string) => void
  onOpenCorrection: () => void
}

export function TranscriptPanel({ live, draft, onDraftChange, onOpenCorrection }: Props) {
  return (
    <section className="panel">
      <header className="panel__head">
        <h2>Transcript</h2>
        <button type="button" className="linkish" onClick={onOpenCorrection}>
          Fix what it heard…
        </button>
      </header>
      <div className="panel__live" aria-live="polite">
        {live ? (
          <p className="live">{live}</p>
        ) : (
          <p className="muted">Live mic off — start the mic or hold Space to talk.</p>
        )}
      </div>
      <label className="sr-only" htmlFor="draft-edit">
        Edit before send
      </label>
      <textarea
        id="draft-edit"
        className="draft"
        rows={4}
        placeholder="What you want to send (edit the transcript or type here)…"
        value={draft}
        onChange={(e) => onDraftChange(e.target.value)}
      />
    </section>
  )
}
