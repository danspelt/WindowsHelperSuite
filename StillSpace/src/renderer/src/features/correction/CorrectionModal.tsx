import { useState } from 'react'
import { saveCorrection } from '../../services/memory/correctionMemory'

type Props = {
  mistaken: string
  onClose: () => void
}

export function CorrectionModal({ mistaken, onClose }: Props) {
  const [corrected, setCorrected] = useState(mistaken)

  function save() {
    if (mistaken.trim() && corrected.trim() && mistaken.trim() !== corrected.trim()) {
      saveCorrection(mistaken, corrected)
    }
    onClose()
  }

  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-label="Transcript correction">
      <div className="modal">
        <header className="modal__head">
          <h2>Correct transcript</h2>
          <button type="button" className="linkish" onClick={onClose}>
            Cancel
          </button>
        </header>
        <div className="modal__body">
          <p className="muted small">What the app heard:</p>
          <pre className="pre">{mistaken || '—'}</pre>
          <label>
            What you actually said
            <textarea rows={4} value={corrected} onChange={(e) => setCorrected(e.target.value)} />
          </label>
          <div className="row">
            <button type="button" className="primary" onClick={save}>
              Save correction for later
            </button>
            <button type="button" onClick={onClose}>
              Close
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
