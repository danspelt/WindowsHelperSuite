export type SttCallbacks = {
  onPartial: (text: string) => void
  onFinal: (text: string) => void
  onError: (message: string) => void
}

export type SttStartOptions = {
  /**
   * When true, after Chrome ends a recognition session (common after silence), restart automatically.
   * Use for hands-free / live mic toggle. Do not use for push-to-hold (e.g. spacebar).
   */
  keepAlive?: boolean
}

function getRecognitionCtor(): (new () => SpeechRecognition) | null {
  const w = window as Window & {
    SpeechRecognition?: new () => SpeechRecognition
    webkitSpeechRecognition?: new () => SpeechRecognition
  }
  return w.SpeechRecognition ?? w.webkitSpeechRecognition ?? null
}

export class WebSpeechStt {
  private rec: SpeechRecognition | null = null
  private inputStream: MediaStream | null = null
  private keepAliveDesired = false
  private restartLang = ''
  private restartCbs: SttCallbacks | null = null

  isAvailable(): boolean {
    return getRecognitionCtor() !== null
  }

  /**
   * Optional: acquire the chosen mic so the OS/STT stack tends to use it (best-effort on Windows).
   */
  async prepareInput(deviceId: string | undefined): Promise<void> {
    this.releaseInput()
    if (!deviceId || !navigator.mediaDevices?.getUserMedia) return
    try {
      this.inputStream = await navigator.mediaDevices.getUserMedia({
        audio: { deviceId: { exact: deviceId } }
      })
    } catch {
      this.inputStream = null
    }
  }

  releaseInput(): void {
    this.inputStream?.getTracks().forEach((t) => t.stop())
    this.inputStream = null
  }

  start(lang: string, cbs: SttCallbacks, options?: SttStartOptions): void {
    const Ctor = getRecognitionCtor()
    if (!Ctor) {
      cbs.onError('Speech recognition is not available in this environment.')
      return
    }
    this.keepAliveDesired = options?.keepAlive ?? false
    this.restartLang = lang
    this.restartCbs = cbs
    this.stopRecognition()
    this.attachRecognition(Ctor, lang, cbs)
  }

  private attachRecognition(Ctor: new () => SpeechRecognition, lang: string, cbs: SttCallbacks): void {
    const r = new Ctor()
    r.lang = lang
    r.interimResults = true
    r.continuous = true
    r.onresult = (ev: SpeechRecognitionEvent) => {
      let interim = ''
      let final = ''
      for (let i = ev.resultIndex; i < ev.results.length; i++) {
        const piece = ev.results[i][0]?.transcript ?? ''
        if (ev.results[i].isFinal) final += piece
        else interim += piece
      }
      const live = (final + interim).trim()
      if (live) cbs.onPartial(live)
      if (final.trim()) cbs.onFinal(final.trim())
    }
    r.onerror = (ev: SpeechRecognitionErrorEvent) => {
      this.keepAliveDesired = false
      cbs.onError(ev.error || 'speech error')
    }
    r.onend = () => {
      if (this.rec === r) this.rec = null
      if (!this.keepAliveDesired || !this.restartCbs) return
      queueMicrotask(() => {
        if (!this.keepAliveDesired || !this.restartCbs) return
        const Next = getRecognitionCtor()
        if (!Next) return
        this.attachRecognition(Next, this.restartLang, this.restartCbs)
      })
    }
    this.rec = r
    try {
      r.start()
    } catch (e) {
      this.keepAliveDesired = false
      cbs.onError(e instanceof Error ? e.message : String(e))
    }
  }

  stop(): void {
    this.keepAliveDesired = false
    this.restartCbs = null
    this.stopRecognition()
    this.releaseInput()
  }

  private stopRecognition(): void {
    if (!this.rec) return
    try {
      this.rec.stop()
    } catch {
      /* ignore */
    }
    this.rec = null
  }
}
