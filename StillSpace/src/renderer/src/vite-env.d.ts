/// <reference types="vite/client" />

import type { ChatMessage } from '../../shared/types'

export type CounselorCompleteResult =
  | { ok: true; text: string }
  | { ok: false; error: string }

export type CounselorTtsResult =
  | { ok: true; base64: string; mime: string }
  | { ok: false; error: string }

declare global {
  interface Window {
    counselor: {
      complete: (messages: ChatMessage[]) => Promise<CounselorCompleteResult>
      tts: (text: string) => Promise<CounselorTtsResult>
    }
  }
}

export {}
