import { uuidv7 } from '../../lib/uuid'

/**
 * A write the user started whose outcome is not known yet. It lives outside any dialog, in sessionStorage and keyed by
 * user and evidence, so closing and reopening a dialog, a retry, or a reload all send the same Idempotency-Key: the
 * server then answers with what it already did instead of doing it twice. It is cleared once the server's answer is
 * known, by the action that got it, so an answer that lands after the page is gone still clears it.
 */
export interface PendingIntent {
  idempotencyKey: string
  /** What was asked, so asking for something else gets a new key (the same key with another body is a 422). */
  fingerprint: string
  startedAt: string
  /** The form as first sent; a retry resends exactly this. */
  fields: Record<string, string>
}

export type IntentKind = 'request' | 'decision'

const PREFIX = 'evidence-chain:intent:'
// Where sessionStorage is blocked, intents still survive dialogs, just not a reload.
const memory = new Map<string, PendingIntent>()

/** One user's request, or decision, on one evidence. Per user, as the server's keys are. */
export function intentScope(kind: IntentKind, userId: number, code: string): string {
  return `${kind}:${userId}:${code}`
}

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
    removeStored(scope) // an older stored intent would otherwise win over this one
  }
  return intent
}

/**
 * The server's answer is known: the next write starts a new intent. With `key`, only the intent sent with that key is
 * cleared, so a late answer cannot clear a newer write's intent.
 */
export function clearIntent(scope: string, key?: string): void {
  if (key !== undefined && getIntent(scope)?.idempotencyKey !== key) return
  memory.delete(scope)
  removeStored(scope)
}

/** Forgets every intent in this tab (sign-out). */
export function clearAllIntents(): void {
  memory.clear()
  try {
    for (const name of Object.keys(sessionStorage)) if (name.startsWith(PREFIX)) sessionStorage.removeItem(name)
  } catch {
    // Nothing stored.
  }
}

function removeStored(scope: string): void {
  try {
    sessionStorage.removeItem(PREFIX + scope)
  } catch {
    // Nothing stored.
  }
}

function fingerprintOf(fields: Record<string, string>): string {
  return JSON.stringify(Object.keys(fields).sort().map((name) => [name, fields[name]]))
}
