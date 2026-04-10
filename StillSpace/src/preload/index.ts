import { contextBridge, ipcRenderer } from 'electron'
import type { ChatMessage } from '../shared/types'

contextBridge.exposeInMainWorld('counselor', {
  complete: (messages: ChatMessage[]) => ipcRenderer.invoke('counselor:complete', { messages }),
  tts: (text: string) => ipcRenderer.invoke('counselor:tts', text)
})
