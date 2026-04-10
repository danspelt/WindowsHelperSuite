/** Shokz OpenRun Pro — match common Windows Bluetooth names */
const OPENRUN_LABEL_RES = [/openrun\s*pro/i, /shokz\s*openrun/i, /openrun\s*pro\s*by\s*shokz/i]

export type AudioDevicePick = {
  deviceId: string
  label: string
}

function norm(s: string): string {
  return s.trim().toLowerCase()
}

export function labelLooksLikeOpenRunPro(label: string): boolean {
  const l = label.trim()
  if (!l) return false
  return OPENRUN_LABEL_RES.some((re) => re.test(l))
}

export async function ensureMicPermission(): Promise<boolean> {
  if (!navigator.mediaDevices?.getUserMedia) return false
  try {
    const s = await navigator.mediaDevices.getUserMedia({ audio: true })
    s.getTracks().forEach((t) => t.stop())
    return true
  } catch {
    return false
  }
}

export async function enumerateAudioDevices(): Promise<{
  inputs: MediaDeviceInfo[]
  outputs: MediaDeviceInfo[]
}> {
  if (!navigator.mediaDevices?.enumerateDevices) {
    return { inputs: [], outputs: [] }
  }
  const all = await navigator.mediaDevices.enumerateDevices()
  return {
    inputs: all.filter((d) => d.kind === 'audioinput'),
    outputs: all.filter((d) => d.kind === 'audiooutput')
  }
}

/**
 * Pick headset output: saved id if still present, else first label match.
 */
export function resolveOpenRunOutput(
  outputs: MediaDeviceInfo[],
  savedOutputDeviceId: string | undefined
): AudioDevicePick | null {
  if (savedOutputDeviceId) {
    const hit = outputs.find((d) => d.deviceId === savedOutputDeviceId)
    if (hit) return { deviceId: hit.deviceId, label: hit.label || 'Saved output device' }
  }
  for (const d of outputs) {
    if (labelLooksLikeOpenRunPro(d.label)) {
      return { deviceId: d.deviceId, label: d.label || 'OpenRun Pro' }
    }
  }
  return null
}

export function audioElementSupportsSetSinkId(): boolean {
  const el = document.createElement('audio')
  return typeof (el as HTMLAudioElement & { setSinkId?: (id: string) => Promise<void> }).setSinkId === 'function'
}

export function subscribeDeviceChange(cb: () => void): () => void {
  const md = navigator.mediaDevices
  if (!md?.addEventListener) return () => {}
  md.addEventListener('devicechange', cb)
  return () => md.removeEventListener('devicechange', cb)
}
