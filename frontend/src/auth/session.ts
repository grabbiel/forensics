/** The three roles the API issues tokens for. */
export type Role = 'Investigador' | 'Custodio' | 'Supervisor'

export interface SessionUser {
  id: number
  userName: string
  displayName: string
  role: Role
}

/** A signed-in user and their bearer token, as POST /api/v1/auth/token returns them. */
export interface Session {
  accessToken: string
  expiresAtUtc: string
  user: SessionUser
}

const STORAGE_KEY = 'evidence-chain:session'
// A token this close to expiry is treated as gone, so a request never starts with one about to lapse.
const EXPIRY_MARGIN_MS = 60_000

// Memory is the source of truth; sessionStorage only lets a reload keep the session (never another tab or window).
let cached: Session | null | undefined

/** The current session, or null when signed out or expired. */
export function getSession(now = Date.now()): Session | null {
  if (cached === undefined) cached = read()
  if (cached && Date.parse(cached.expiresAtUtc) - EXPIRY_MARGIN_MS <= now) clearSession()
  return cached ?? null
}

export function saveSession(session: Session): void {
  cached = session
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  } catch {
    // Storage blocked (private mode, quota): the session still lives in memory.
  }
}

export function clearSession(): void {
  cached = null
  try {
    sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // Nothing stored to remove.
  }
}

function read(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Session) : null
  } catch {
    return null
  }
}
