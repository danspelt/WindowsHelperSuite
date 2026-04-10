import { app, BrowserWindow, ipcMain } from 'electron'
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { config as loadEnv } from 'dotenv'
import OpenAI from 'openai'

// Windows: avoid default GPU/disk cache paths that can hit "Access is denied" with AV or multiple dev instances.
if (process.platform === 'win32') {
  try {
    const cacheRoot = join(app.getPath('userData'), 'chromium-cache')
    mkdirSync(cacheRoot, { recursive: true })
    app.commandLine.appendSwitch('disk-cache-dir', cacheRoot)
  } catch {
    /* ignore */
  }
  app.commandLine.appendSwitch('disable-gpu-shader-disk-cache')
}

loadEnv({ path: join(app.isPackaged ? app.getAppPath() : process.cwd(), '.env') })

let mainWindow: BrowserWindow | null = null

function getOpenAI(): OpenAI | null {
  const key = process.env.OPENAI_API_KEY?.trim()
  if (!key) return null
  return new OpenAI({ apiKey: key })
}

function createWindow(): void {
  mainWindow = new BrowserWindow({
    width: 1280,
    height: 800,
    minWidth: 960,
    minHeight: 640,
    title: 'Still Space',
    backgroundColor: '#0f1218',
    autoHideMenuBar: true,
    webPreferences: {
      preload: join(__dirname, '../preload/index.mjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  })

  mainWindow.on('closed', () => {
    mainWindow = null
  })

  if (process.env.ELECTRON_RENDERER_URL) {
    void mainWindow.loadURL(process.env.ELECTRON_RENDERER_URL)
  } else {
    void mainWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

app.whenReady().then(() => {
  createWindow()
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

ipcMain.handle(
  'counselor:complete',
  async (_evt, payload: { messages: { role: 'system' | 'user' | 'assistant'; content: string }[] }) => {
    const client = getOpenAI()
    if (!client) {
      return {
        ok: false as const,
        error: 'Missing OPENAI_API_KEY in .env (see .env.example).'
      }
    }
    try {
      const model = process.env.OPENAI_CHAT_MODEL?.trim() || 'gpt-4o-mini'
      const completion = await client.chat.completions.create({
        model,
        messages: payload.messages,
        temperature: 0.65,
        max_tokens: 900
      })
      const text = completion.choices[0]?.message?.content?.trim() ?? ''
      return { ok: true as const, text }
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e)
      return { ok: false as const, error: msg }
    }
  }
)

ipcMain.handle('counselor:tts', async (_evt, text: string) => {
  const client = getOpenAI()
  if (!client || !text.trim()) return { ok: false as const, error: 'no_key_or_text' }
  try {
    const model = process.env.OPENAI_TTS_MODEL?.trim() || 'tts-1'
    const voice = (process.env.OPENAI_TTS_VOICE?.trim() || 'alloy') as
      | 'alloy'
      | 'echo'
      | 'fable'
      | 'onyx'
      | 'nova'
      | 'shimmer'
    const mp3 = await client.audio.speech.create({
      model,
      voice,
      input: text.slice(0, 4000)
    })
    const buf = Buffer.from(await mp3.arrayBuffer())
    return { ok: true as const, base64: buf.toString('base64'), mime: 'audio/mpeg' }
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e)
    return { ok: false as const, error: msg }
  }
})
