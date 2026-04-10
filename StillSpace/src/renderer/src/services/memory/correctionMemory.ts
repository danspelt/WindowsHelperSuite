export type CorrectionEntry = {
  id: string
  at: number
  mistaken: string
  corrected: string
  context?: string
}

const KEY = 'still-space.corrections.v1'
const MAX = 200

function uid(): string {
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 9)}`
}

export function loadCorrections(): CorrectionEntry[] {
  try {
    const raw = localStorage.getItem(KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as CorrectionEntry[]
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

export function saveCorrection(mistaken: string, corrected: string, context?: string): CorrectionEntry {
  const list = loadCorrections()
  const entry: CorrectionEntry = {
    id: uid(),
    at: Date.now(),
    mistaken: mistaken.trim(),
    corrected: corrected.trim(),
    context: context?.trim()
  }
  list.unshift(entry)
  localStorage.setItem(KEY, JSON.stringify(list.slice(0, MAX)))
  return entry
}

/** Simple glossary: mistaken -> corrected from recent history */
export function correctionGlossary(): Record<string, string> {
  const map: Record<string, string> = {}
  for (const e of loadCorrections()) {
    const k = e.mistaken.toLowerCase()
    if (k && e.corrected) map[k] = e.corrected
  }
  return map
}

export function applyGlossaryToTranscript(text: string): string {
  let out = text
  const glossary = correctionGlossary()
  for (const [wrong, right] of Object.entries(glossary)) {
    if (!wrong) continue
    const re = new RegExp(`\\b${escapeRegExp(wrong)}\\b`, 'gi')
    out = out.replace(re, right)
  }
  return out
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}
