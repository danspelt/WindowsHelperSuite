import { create } from 'zustand'
import { persist } from 'zustand/middleware'

type SettingsState = {
  preferredName: string
  sttLang: string
  autoReadAloud: boolean
  preferOpenAiTts: boolean
  pauseBeforeReplyMs: number
  /** When true, counselor audio only via matched OpenRun output + setSinkId; never browser TTS to default device. */
  headsetOnlyMode: boolean
  /** Saved audiooutput deviceId (manual bind if name match fails). */
  preferredOutputDeviceId: string
  /** Preferred mic for STT (best-effort). */
  preferredInputDeviceId: string
  setPreferredName: (v: string) => void
  setSttLang: (v: string) => void
  setAutoReadAloud: (v: boolean) => void
  setPreferOpenAiTts: (v: boolean) => void
  setPauseBeforeReplyMs: (v: number) => void
  setHeadsetOnlyMode: (v: boolean) => void
  setPreferredOutputDeviceId: (v: string) => void
  setPreferredInputDeviceId: (v: string) => void
}

export const useSettingsStore = create<SettingsState>()(
  persist(
    (set) => ({
      preferredName: '',
      sttLang: 'en-US',
      autoReadAloud: true,
      preferOpenAiTts: true,
      pauseBeforeReplyMs: 400,
      headsetOnlyMode: false,
      preferredOutputDeviceId: '',
      preferredInputDeviceId: '',
      setPreferredName: (v) => set({ preferredName: v }),
      setSttLang: (v) => set({ sttLang: v }),
      setAutoReadAloud: (v) => set({ autoReadAloud: v }),
      setPreferOpenAiTts: (v) => set({ preferOpenAiTts: v }),
      setPauseBeforeReplyMs: (v) => set({ pauseBeforeReplyMs: v }),
      setHeadsetOnlyMode: (v) => set({ headsetOnlyMode: v }),
      setPreferredOutputDeviceId: (v) => set({ preferredOutputDeviceId: v }),
      setPreferredInputDeviceId: (v) => set({ preferredInputDeviceId: v })
    }),
    { name: 'still-space.settings.v2' }
  )
)
