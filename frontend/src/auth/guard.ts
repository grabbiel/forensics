import { redirect } from 'react-router'
import { ApiError } from '../api/client'
import { clearSession, getSession, type Session } from './session'

/** Pages a user can be sent back to after signing in: never /login, /logout or a resource route. */
const PAGE = /^\/(?:evidence\/[^/?#]+)?(?:[?#].*)?$/

/** /login, remembering the page to come back to. */
function loginPath(back: string): string {
  return back === '/' ? '/login' : `/login?redirectTo=${encodeURIComponent(back)}`
}

/** The page a request belongs to: its own URL for a page, else the page a resource route says it serves. */
function pageOf(request: Request, page?: string): string {
  const url = new URL(request.url)
  return page ?? `${url.pathname}${url.search}`
}

/** The session, or a redirect to /login when there is none. */
export function requireSession(request: Request, page?: string): Session {
  const session = getSession()
  if (!session) throw redirect(loginPath(safeRedirect(pageOf(request, page))))
  return session
}

/**
 * Runs a loader or action body for a signed-in user. A 401 means the token was refused (expired or the API's key
 * changed): forget it and send the user to sign in again. Resource routes pass `page`, the page the user is on, so
 * signing in returns there and not to a URL that renders nothing.
 */
export async function signedIn<T>(request: Request, run: (session: Session) => Promise<T>, page?: string): Promise<T> {
  const session = requireSession(request, page)
  try {
    return await run(session)
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      clearSession()
      throw redirect(loginPath(safeRedirect(pageOf(request, page))))
    }
    throw error
  }
}

/** Only pages of this site: absolute URLs, "//host", "/\\host", /login and resource routes fall back to the inbox. */
export function safeRedirect(target: string | null): string {
  return target && PAGE.test(target) ? target : '/'
}

/** Page loaders with no data still need the guard, so a signed-out visitor never sees the app frame. */
export function guardLoader({ request }: { request: Request }) {
  requireSession(request)
  return null
}
