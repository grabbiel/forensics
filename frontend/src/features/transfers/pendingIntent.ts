import { uuidv7 } from '../../lib/uuid'

/**
 * A write the user started whose outcome is not known yet. It lives outside any dialog, in sessionStorage and keyed by
 * evidence, so closing and reopening a dialog, a retry, or a reload all send the same Idempotency-Key: the server then
 * answers with what it already did instead of doing it twice. It is cleared once the server's answer is known.
 */
export interface PendingIntent {
  idempotencyKey: string
  /** What was asked, so asking for something else gets a new key (the same key with another body is a 422). */
  fingerprint: string
  startedAt: string
  /** The form as first sent; a retry resends exactly this. */
  fields: Record<string, string>
}

const PREFIX = 'evidence-chain:intent:'
// Where sessionStorage is blocked, intents still survive dialogs, just not a reload.
const memory = new Map<string, PendingIntent>()

export function getIntent(scope: string): PendingIntent | undefined {
  try {
    const raw = sessionStorage.getItem(PREFIX + scope)
    if (raw) return JSON.parse(raw) as PendingIntent
  } catch {
    // Unreadable storage: fall back to memory.
  }
  return memory.get(scope)
}

/** The intent for this write: the existing one when the same write is sent again, else a new one with a new key. */
export function startIntent(scope: string, fields: Record<string, string>, now = new Date()): PendingIntent {
  const fingerprint = fingerprintOf(fields)
  const existing = getIntent(scope)
  if (existing?.fingerprint === fingerprint) return existing

  const intent: PendingIntent = { idempotencyKey: uuidv7(now.getTime()), fingerprint, startedAt: now.toISOString(), fields }
  memory.set(scope, intent)
  try {
    sessionStorage.setItem(PREFIX + scope, JSON.stringify(intent))
  } catch {
    // Kept in memory only.
  }
  return intent
}

/** The server's answer is known (done or refused): the next write starts a new intent. */
export function clearIntent(scope: string): void {
  memory.delete(scope)
  try {
    sessionStorage.removeItem(PREFIX + scope)
  } catch {
    // Nothing stored.
  }
}

function fingerprintOf(fields: Record<string, string>): string {
  return JSON.stringify(Object.keys(fields).sort().map((name) => [name, fields[name]]))
}
