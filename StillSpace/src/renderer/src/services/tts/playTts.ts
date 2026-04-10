let currentPlayback: HTMLAudioElement | null = null

export function stopAllCounselorAudio(): void {
  if (currentPlayback) {
    try {
      currentPlayback.pause()
      currentPlayback.removeAttribute('src')
      currentPlayback.load()
    } catch {
      /* ignore */
    }
    currentPlayback = null
  }
  window.speechSynthesis.cancel()
}

export type PlayOpenAiOptions = {
  /** Route playback to this output (e.g. OpenRun Pro). Requires Chromium/Electron `setSinkId`. */
  sinkId?: string
  /** If true, never play if `setSinkId` fails — avoids leaking audio to the default device. */
  blockOnSinkFailure?: boolean
}

export async function playOpenAiTts(text: string, opts?: PlayOpenAiOptions): Promise<boolean> {
  const res = await window.counselor.tts(text)
  if (!res.ok) return false
  const bytes = Uint8Array.from(atob(res.base64), (c) => c.charCodeAt(0))
  const blob = new Blob([bytes], { type: res.mime })
  const url = URL.createObjectURL(blob)
  const audio = new Audio(url)
  currentPlayback = audio
  if (opts?.sinkId && typeof audio.setSinkId === 'function') {
    try {
      await audio.setSinkId(opts.sinkId)
    } catch {
      URL.revokeObjectURL(url)
      currentPlayback = null
      if (opts.blockOnSinkFailure) return false
    }
  } else if (opts?.sinkId && opts.blockOnSinkFailure) {
    URL.revokeObjectURL(url)
    currentPlayback = null
    return false
  }
  try {
    await audio.play()
    await new Promise<void>((resolve, reject) => {
      audio.onended = () => resolve()
      audio.onerror = () => reject(new Error('audio playback error'))
    })
    return true
  } catch {
    return false
  } finally {
    if (currentPlayback === audio) currentPlayback = null
    URL.revokeObjectURL(url)
  }
}

export function speakWithBrowserTts(text: string, rate = 0.95, pitch = 1): void {
  window.speechSynthesis.cancel()
  const u = new SpeechSynthesisUtterance(text)
  u.rate = rate
  u.pitch = pitch
  window.speechSynthesis.speak(u)
}
