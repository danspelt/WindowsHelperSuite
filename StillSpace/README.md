# Still Space

Windows desktop counselor MVP: **Electron + React + React Three Fiber**, voice and text, local transcript corrections, optional **OpenAI** chat and TTS (API key stays in the main process).

## Run

```powershell
cd StillSpace
copy .env.example .env
# Add OPENAI_API_KEY to .env for cloud replies + neural TTS
npm install
npm run dev
```

## Build

```powershell
npm run build
```

The packaged layout loads preload from `out/preload/index.mjs`.

## What is implemented

- **OpenRun Pro–only mode** (Settings): detect Shokz output by name or a **saved output device id**, route counselor TTS with **`setSinkId`**, never fall back to laptop speakers; pause mic + audio if the headset drops; **text-only session** escape hatch
- Welcome + session flow, settings (modes, STT language, TTS prefs, audio panel)
- **Web Speech API** dictation (spacebar hold outside text fields, or mic toggle), live transcript + editable draft; optional **preferred input device** (best-effort `getUserMedia` priming)
- **Correction modal** → saves mistaken vs corrected phrases in **localStorage** and applies simple glossary passes before send
- **Crisis keyword banner** (not a clinical assessment)
- **3D room** + stylized seated figure, idle breathing / listen nod / simple “jaw” motion during OpenAI TTS playback
- **IPC** to OpenAI for chat completions and `tts-1` speech when a key is present. With **OpenRun-only** on, TTS uses **only** OpenAI audio + `setSinkId` to the headset (no `speechSynthesis` fallback, so no accidental speaker output)

## Not in this MVP

Long-term encrypted store, Whisper/Azure STT pipelines, lip-sync to visemes, session summaries UI, and clinical compliance review — add in later phases.

## Note

This is **not** therapy or medical advice. It is a software shell for calm conversation.
