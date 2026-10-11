import { useEffect, useRef } from 'react'
import { useFetcher, useLocation } from 'react-router'

/**
 * Link state for opening an unread notification: the page it opens marks it read once loaded. Marking it while the
 * link navigates would make the router load the new page twice, because a fetcher action that finishes during a
 * navigation loads every route the next page adds, whatever their shouldRevalidate says.
 */
export const readOnArrival = (notificationId: number) => ({ readNotification: notificationId })

/**
 * Submits the mark-read a link's readOnArrival state asks for, once per history entry. Mounted in the header, which
 * outlives every page. The success reloads the bell; the evidence page ignores it (see routes.tsx).
 */
export function useReadOnArrival(): void {
  const { key, state } = useLocation()
  const { submit } = useFetcher()
  const handled = useRef<string>(undefined)

  useEffect(() => {
    const id = (state as { readNotification?: unknown } | null)?.readNotification
    if (typeof id !== 'number' || handled.current === key) return
    handled.current = key
    void submit({ id: String(id) }, { method: 'post', action: '/notifications/read' })
  }, [key, state, submit])
}
