import { redirect } from 'react-router'
import { ApiError } from '../api/client'
import { clearSession, getSession, type Session } from './session'

/** /login, remembering where to come back to. */
export function loginPath(request: Request): string {
  const url = new URL(request.url)
  const back = `${url.pathname}${url.search}`
  return back === '/' ? '/login' : `/login?redirectTo=${encodeURIComponent(back)}`
}

/** The session, or a redirect to /login when there is none. */
export function requireSession(request: Request): Session {
  const session = getSession()
  if (!session) throw redirect(loginPath(request))
  return session
}

/**
 * Runs a loader or action body for a signed-in user. A 401 means the token was refused (expired or the API's key
 * changed): forget it and send the user to sign in again, back to the same place afterwards.
 */
export async function signedIn<T>(request: Request, run: (session: Session) => Promise<T>): Promise<T> {
  const session = requireSession(request)
  try {
    return await run(session)
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      clearSession()
      throw redirect(loginPath(request))
    }
    throw error
  }
}

/** Only same-site paths: "//evil.example" and absolute URLs fall back to the inbox. */
export function safeRedirect(target: string | null): string {
  return target && target.startsWith('/') && !target.startsWith('//') && !target.startsWith('/\\') ? target : '/'
}
