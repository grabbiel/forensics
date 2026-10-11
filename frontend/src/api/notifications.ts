import { getJson, postJson } from './client'
import type { PersonRef } from './evidence'

export type NotificationKind = 'TransferRequested' | 'TransferAccepted' | 'TransferRejected'

/** What happened to one transfer, as its recipient reads it. The SPA words it; the API sends facts only. */
export interface NotificationItem {
  notificationId: number
  kind: NotificationKind
  createdAtUtc: string
  readAtUtc: string | null
  evidenceCode: string
  transferId: number
  requestedBy: PersonRef
  /** The holder when requested. */
  from: PersonRef
  /** The recipient, who decides. */
  to: PersonRef
  reason: string
  /** A rejection's reason, or an acceptance's notes; null while undecided. */
  decisionNotes: string | null
}

export interface NotificationPage {
  items: NotificationItem[]
  /** Opaque; the next (older) page. Null on the last page. */
  nextCursor: string | null
  unreadCount: number
}

/** One page of the signed-in user's notifications, newest first. A cursor the API refuses is a 400 on `cursor`. */
export function listNotifications(cursor: string | undefined, signal?: AbortSignal): Promise<NotificationPage> {
  const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  return getJson<NotificationPage>(`/api/v1/notifications${query}`, signal)
}

/**
 * The signed-in user's unread count. The answer is checked, not trusted: a body without a numeric unreadCount is a
 * failed read (TypeError), so a stub that answers every URL alike cannot pass for a count.
 */
export async function getUnreadCount(signal?: AbortSignal): Promise<number> {
  const { unreadCount } = await getJson<{ unreadCount?: unknown }>('/api/v1/notifications/unread-count', signal)
  if (typeof unreadCount !== 'number') throw new TypeError('The unread count answer has no count.')
  return unreadCount
}

/** Marks one read; again is fine. Someone else's id, or an unknown one, is a 404. */
export async function markRead(id: number): Promise<void> {
  await postJson<void>(`/api/v1/notifications/${id}/read`, {})
}

/** Marks read everything up to `upToId`, the newest the user has seen; anything newer stays unread. */
export async function markAllRead(upToId: number): Promise<void> {
  await postJson<void>('/api/v1/notifications/read', { upToId })
}
