import { ArrowsLeftRight, BellSimple, CaretRight, CheckCircle, XCircle } from '@phosphor-icons/react'
import { useRef, type MouseEvent } from 'react'
import { Link, useFetcher, useLoaderData, useNavigation } from 'react-router'
import type { NotificationItem, NotificationKind } from '../../api/notifications'
import type { SessionUser } from '../../auth/session'
import { formatUtcDateTime } from '../../lib/format'
import type { notificationsLoader } from './notificationRoutes'
import { readOnArrival } from './readOnArrival'
import { sentenceFor, type Part } from './sentence'

const KIND_ICONS: Record<NotificationKind, typeof ArrowsLeftRight> = {
  TransferRequested: ArrowsLeftRight,
  TransferAccepted: CheckCircle,
  TransferRejected: XCircle,
}

/**
 * The signed-in user's notifications, newest first. Opening the page marks nothing read: reading is explicit, by
 * opening an item or with "Marcar todas como leídas", which sends the newest id on this page so anything that arrived
 * since stays unread. The cursor lives in the URL, as in the inbox.
 */
export function NotificationsPage() {
  const { items, nextCursor, cursor, viewer } = useLoaderData<typeof notificationsLoader>()
  const loading = useNavigation().state === 'loading'
  const titleRef = useRef<HTMLHeadingElement>(null)
  const markAll = useFetcher()
  const marking = markAll.state !== 'idle'

  /** Pages from a link that may not exist on the next page, so focus goes to the heading; not while a load runs. */
  function page(event: MouseEvent) {
    if (loading) event.preventDefault()
    else titleRef.current?.focus()
  }

  return (
    <section className="panel" aria-labelledby="notifications-title">
      <div className="panel__head">
        <h1 id="notifications-title" className="panel__title" ref={titleRef} tabIndex={-1}>
          Notificaciones
        </h1>
        {items.some((item) => !item.readAtUtc) && (
          <markAll.Form method="post" action="/notifications/read" className="notes__mark-all" onSubmit={(event) => marking && event.preventDefault()}>
            <input type="hidden" name="upToId" value={items[0].notificationId} />
            <button type="submit" className="button button--ghost" aria-disabled={marking}>
              Marcar todas como leídas
            </button>
          </markAll.Form>
        )}
      </div>

      <div className="results" aria-busy={loading}>
        {items.length === 0 ? (
          <div className="empty">
            <BellSimple size={32} aria-hidden="true" />
            <p className="empty__title">{cursor ? 'No hay notificaciones más antiguas.' : 'No tienes notificaciones.'}</p>
          </div>
        ) : (
          <ul className="notes">
            {items.map((item) => (
              <NotificationRow key={item.notificationId} item={item} viewer={viewer} />
            ))}
          </ul>
        )}
      </div>

      {(cursor || nextCursor) && (
        <nav className="pager" aria-label="Páginas de notificaciones">
          {cursor && (
            <Link to="/notifications" className="button button--ghost" aria-disabled={loading} onClick={page}>
              Más recientes
            </Link>
          )}
          {nextCursor && (
            <Link to={`/notifications?cursor=${encodeURIComponent(nextCursor)}`} className="button button--ghost" aria-disabled={loading} onClick={page}>
              Ver anteriores
              <CaretRight size={16} aria-hidden="true" />
            </Link>
          )}
        </nav>
      )}
    </section>
  )
}

/** One item: a real link to the evidence. Opening an unread one marks it read once the evidence page has loaded. */
function NotificationRow({ item, viewer }: { item: NotificationItem; viewer: SessionUser }) {
  const sentence = sentenceFor(item, viewer)
  const Icon = KIND_ICONS[item.kind]
  const unread = !item.readAtUtc

  return (
    <li className={unread ? 'note note--unread' : 'note'}>
      <Icon className="note__icon" size={20} aria-hidden="true" />
      <div className="note__body">
        <Link to={`/evidence/${encodeURIComponent(item.evidenceCode)}`} state={unread ? readOnArrival(item.notificationId) : undefined} className="note__link">
          {unread && (
            <>
              <span className="visually-hidden">No leída.</span>{' '}
            </>
          )}
          <Parts parts={sentence.parts} />
        </Link>
        {sentence.detail && <p className="note__detail">{sentence.detail}</p>}
        <time className="note__time" dateTime={item.createdAtUtc}>
          {formatUtcDateTime(item.createdAtUtc)}
        </time>
      </div>
    </li>
  )
}

function Parts({ parts }: { parts: Part[] }) {
  return parts.map((part, index) => (typeof part === 'string' ? part : <strong key={index}>{part.strong}</strong>))
}
