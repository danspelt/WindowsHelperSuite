import { create } from 'zustand'
import type { CounselorMode } from '../prompts/counselorPrompts'

export type ChatTurn = { role: 'user' | 'assistant'; content: string }

type SessionState = {
  started: boolean
  /** Typing-only session: no mic, no TTS (privacy / headset unavailable). */
  textOnlySession: boolean
  mode: CounselorMode
  draftUserText: string
  liveTranscript: string
  listening: boolean
  history: ChatTurn[]
  lastAssistant: string
  pendingSendText: string | null
  startSession: (opts?: { textOnly?: boolean }) => void
  setTextOnlySession: (v: boolean) => void
  endSession: () => void
  setMode: (m: CounselorMode) => void
  setDraftUserText: (t: string) => void
  setLiveTranscript: (t: string) => void
  setListening: (v: boolean) => void
  appendUser: (content: string) => void
  setAssistant: (content: string) => void
  clearPending: () => void
  setPendingSendText: (t: string | null) => void
}

export const useSessionStore = create<SessionState>((set) => ({
  started: false,
  textOnlySession: false,
  mode: 'support',
  draftUserText: '',
  liveTranscript: '',
  listening: false,
  history: [],
  lastAssistant: '',
  pendingSendText: null,
  startSession: (opts) =>
    set({
      started: true,
      textOnlySession: Boolean(opts?.textOnly),
      history: [],
      lastAssistant: '',
      draftUserText: '',
      liveTranscript: '',
      pendingSendText: null
    }),
  setTextOnlySession: (v) => set({ textOnlySession: v }),
  endSession: () =>
    set({
      started: false,
      textOnlySession: false,
      listening: false,
      liveTranscript: '',
      draftUserText: ''
    }),
  setMode: (m) => set({ mode: m }),
  setDraftUserText: (t) => set({ draftUserText: t }),
  setLiveTranscript: (t) => set({ liveTranscript: t }),
  setListening: (v) => set({ listening: v }),
  appendUser: (content) =>
    set((s) => ({
      history: [...s.history, { role: 'user', content }],
      draftUserText: '',
      liveTranscript: ''
    })),
  setAssistant: (content) =>
    set((s) => ({
      lastAssistant: content,
      history: [...s.history, { role: 'assistant', content }]
    })),
  clearPending: () => set({ pendingSendText: null }),
  setPendingSendText: (t) => set({ pendingSendText: t })
}))
