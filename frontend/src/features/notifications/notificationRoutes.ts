import type { LoaderFunctionArgs } from 'react-router'
import { ApiError } from '../../api/client'
import { getUnreadCount } from '../../api/notifications'
import { signedIn } from '../../auth/guard'

/** What the unread poll answers. It is never thrown, so a failed poll leaves the header as it was. */
export type UnreadResult = { ok: true; unreadCount: number } | { ok: false; status: number; retryAfterSeconds?: number }

/**
 * Resource route for the bell's fetcher.load: `/notifications/unread?page=<the page the user is on>`. A 401 rethrows
 * through signedIn with that page, which safeRedirect filters, so signing in returns there. Every other failure,
 * including a body that is not a count, is returned as a result.
 */
export function unreadLoader({ request }: LoaderFunctionArgs): Promise<UnreadResult> {
  const page = new URL(request.url).searchParams.get('page') ?? '/'
  return signedIn(request, async () => {
    try {
      return { ok: true, unreadCount: await getUnreadCount(request.signal) }
    } catch (error) {
      if (error instanceof ApiError && error.status !== 401) return { ok: false, status: error.status, retryAfterSeconds: error.retryAfterSeconds }
      if (error instanceof ApiError) throw error
      return { ok: false, status: 0 } // no answer, or not a count
    }
  }, page)
}
