import type { ChatMessage } from '../../../../shared/types'
import { buildSystemMessages } from '../../prompts/counselorPrompts'
import type { CounselorMode } from '../../prompts/counselorPrompts'

export async function sendCounselorMessage(
  mode: CounselorMode,
  history: { role: 'user' | 'assistant'; content: string }[],
  userText: string
): Promise<{ ok: true; text: string } | { ok: false; error: string }> {
  const systems = buildSystemMessages(mode)
  const messages: ChatMessage[] = [
    ...systems,
    ...history.map((m) => ({ role: m.role, content: m.content })),
    { role: 'user', content: userText }
  ]
  const res = await window.counselor.complete(messages)
  return res
}
