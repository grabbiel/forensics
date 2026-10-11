import { redirect, type ActionFunctionArgs, type LoaderFunctionArgs } from 'react-router'
import { ApiError } from '../../api/client'
import { getUnreadCount, listNotifications, markAllRead, markRead } from '../../api/notifications'
import { signedIn } from '../../auth/guard'

/**
 * One page of notifications and who is reading them, so the page can word each item. The cursor lives in the URL. A
 * cursor the API refuses (an old link) sends the user to the first page instead of an error retrying cannot fix.
 */
export function notificationsLoader({ request }: LoaderFunctionArgs) {
  const cursor = new URL(request.url).searchParams.get('cursor') || undefined
  return signedIn(request, async (session) => {
    try {
      const page = await listNotifications(cursor, request.signal)
      return { items: page.items, nextCursor: page.nextCursor, cursor, viewer: session.user }
    } catch (error) {
      if (cursor && error instanceof ApiError && error.status === 400 && error.problem?.errors?.cursor) throw redirect('/notifications')
      throw error
    }
  })
}

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
      return failure(error)
    }
  }, page)
}

/** What a mark-read submit answers; the page ignores failures (the item just stays unread). */
export type ReadResult = { ok: true } | { ok: false; status: number }

/**
 * Resource route `/notifications/read`, for fire-and-forget fetcher submits. Form field `id` marks one, `upToId` marks
 * everything up to it: one task (reading) with two scopes. A success reloads loaded fetchers, so the bell refreshes.
 */
export async function readAction({ request }: ActionFunctionArgs): Promise<ReadResult> {
  const form = await request.formData()
  const id = Number(form.get('id'))
  const upToId = Number(form.get('upToId'))
  return signedIn(request, async () => {
    try {
      if (id > 0) await markRead(id)
      else if (upToId > 0) await markAllRead(upToId)
      else return { ok: false, status: 400 }
      return { ok: true }
    } catch (error) {
      return failure(error)
    }
  }, '/notifications')
}

/** A failed call as a result; a 401 rethrows, so signedIn sends the user to sign in. */
function failure(error: unknown): { ok: false; status: number; retryAfterSeconds?: number } {
  if (error instanceof ApiError && error.status === 401) throw error
  if (error instanceof ApiError) return { ok: false, status: error.status, retryAfterSeconds: error.retryAfterSeconds }
  return { ok: false, status: 0 } // no answer, or not a count
}
