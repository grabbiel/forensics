import { describe, expect, it } from 'vitest'
import { badgeText, bellLabel } from './badge'
import { keepLastCount, nextPollDelayMs, POLL_INTERVAL_MS } from './useUnreadCount'

describe('the unread poll', () => {
  it('asks every 30 s while the tab is visible, and not at all while it is hidden', () => {
    expect(nextPollDelayMs(undefined, true)).toBe(POLL_INTERVAL_MS)
    expect(nextPollDelayMs({ ok: true, unreadCount: 3 }, true)).toBe(30_000)
    expect(nextPollDelayMs({ ok: true, unreadCount: 3 }, false)).toBeNull()
    expect(nextPollDelayMs({ ok: false, status: 503 }, true)).toBe(30_000)
  })

  it('waits as long as a 429 asks, when that is longer', () => {
    expect(nextPollDelayMs({ ok: false, status: 429, retryAfterSeconds: 90 }, true)).toBe(90_000)
    expect(nextPollDelayMs({ ok: false, status: 429, retryAfterSeconds: 5 }, true)).toBe(30_000)
    expect(nextPollDelayMs({ ok: false, status: 429 }, true)).toBe(30_000)
  })

  it('keeps the last good count through a failed poll', () => {
    expect(keepLastCount(undefined, { ok: true, unreadCount: 2 })).toBe(2)
    expect(keepLastCount(2, { ok: false, status: 0 })).toBe(2)
    expect(keepLastCount(2, { ok: true, unreadCount: 0 })).toBe(0)
    expect(keepLastCount(undefined, undefined)).toBeUndefined()
  })
})

describe('the bell', () => {
  it('shows no badge at zero or before the first answer, and caps it at 99+', () => {
    expect(badgeText(undefined)).toBeNull()
    expect(badgeText(0)).toBeNull()
    expect(badgeText(7)).toBe('7')
    expect(badgeText(99)).toBe('99')
    expect(badgeText(100)).toBe('99+')
  })

  it('says the exact count in its name', () => {
    expect(bellLabel(undefined)).toBe('Notificaciones')
    expect(bellLabel(0)).toBe('Notificaciones')
    expect(bellLabel(140)).toBe('Notificaciones, 140 sin leer')
  })
})
