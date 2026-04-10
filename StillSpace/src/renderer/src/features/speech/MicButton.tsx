type Props = {
  listening: boolean
  disabled?: boolean
  onToggle: () => void
}

export function MicButton({ listening, disabled, onToggle }: Props) {
  return (
    <button
      type="button"
      className={`mic-btn ${listening ? 'mic-btn--on' : ''}`}
      disabled={disabled}
      onClick={onToggle}
      title="Hands-free: stays on until you stop. Hold Space for push-to-talk."
      aria-pressed={listening}
    >
      {listening ? 'Stop live mic' : 'Start live mic'}
    </button>
  )
}
