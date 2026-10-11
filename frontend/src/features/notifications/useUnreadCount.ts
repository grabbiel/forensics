import { useEffect, useRef, useState } from 'react'
import { useFetcher, useLocation } from 'react-router'
import { getSession } from '../../auth/session'
import type { UnreadResult } from './notificationRoutes'

/** How often the header asks while the tab is visible: one read, while the per-user Reads bucket refills 60 meanwhile. */
export const POLL_INTERVAL_MS = 30_000

/**
 * When to poll next after `result`: in POLL_INTERVAL_MS, or after a 429's wait if that is longer. Null while the tab is
 * hidden; the visibility listener restarts polling. Pure, so the cadence is tested without timers.
 */
export function nextPollDelayMs(result: UnreadResult | undefined, visible: boolean): number | null {
  if (!visible) return null
  const wait = result?.ok === false && result.status === 429 ? (result.retryAfterSeconds ?? 0) * 1000 : 0
  return Math.max(POLL_INTERVAL_MS, wait)
}

/** The count to show: the new one when the poll answered, otherwise the last good one (a failed poll is silent). */
export function keepLastCount(previous: number | undefined, result: UnreadResult | undefined): number | undefined {
  return result?.ok ? result.unreadCount : previous
}

/**
 * The signed-in user's unread count, kept fresh. It loads on mount, then again after each answer by the delay
 * nextPollDelayMs gives, and once more when the tab becomes visible; it stops when there is no session. The poll is a
 * fetcher.load on the `/notifications/unread` resource route, so the router reloads it after every successful action
 * (the user's own transfer decisions and mark-read submits), and it never drives useNavigation, so the header's
 * progress bar does not flicker. The poll URL carries the current page, so a 401 sends the user to sign in and back.
 */
export function useUnreadCount(): number | undefined {
  const fetcher = useFetcher<UnreadResult>()
  const { pathname, search } = useLocation()
  const poll = useRef(() => {})
  const [shown, setShown] = useState<{ result?: UnreadResult; count?: number }>({})
  if (fetcher.data !== shown.result) setShown({ result: fetcher.data, count: keepLastCount(shown.count, fetcher.data) })

  useEffect(() => {
    poll.current = () => {
      if (getSession()) fetcher.load(`/notifications/unread?page=${encodeURIComponent(pathname + search)}`)
    }
  })

  useEffect(() => {
    poll.current()
    const onVisible = () => document.visibilityState === 'visible' && poll.current()
    document.addEventListener('visibilitychange', onVisible)
    return () => document.removeEventListener('visibilitychange', onVisible)
  }, [])

  useEffect(() => {
    const delay = nextPollDelayMs(fetcher.data, document.visibilityState === 'visible')
    if (delay === null) return
    const timer = setTimeout(() => poll.current(), delay)
    return () => clearTimeout(timer)
  }, [fetcher.data])

  return shown.count
}
