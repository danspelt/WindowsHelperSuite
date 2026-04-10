const crisisPatterns: RegExp[] = [
  /\b(kill myself|end it all|suicid|want to die|better off dead)\b/i,
  /\b(can't go on|cannot go on|no point (in )?living)\b/i,
  /\b(hurt someone|harm (them|him|her)|going to kill)\b/i
]

export type CrisisLevel = 'none' | 'elevated'

export function detectCrisis(text: string): CrisisLevel {
  const t = text.trim()
  if (!t) return 'none'
  for (const re of crisisPatterns) {
    if (re.test(t)) return 'elevated'
  }
  return 'none'
}

export const crisisResources =
  'If you are in immediate danger, please contact local emergency services. In the U.S., you can call or text 988 (Suicide & Crisis Lifeline). If you can, reach someone you trust who can be with you in person.'
