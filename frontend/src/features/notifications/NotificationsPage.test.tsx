import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it, vi } from 'vitest'
import type { EvidenceDetail } from '../../api/evidence'
import type { NotificationItem, NotificationPage } from '../../api/notifications'
import { routes } from '../../routes'
import { diego, lucia, nuria } from '../../test/fixtures'
import { server, setupMsw } from '../../test/msw'
import { signInAs } from '../../test/session'

// The page, the bell and the mark-read route through the real router, fetch and API client; MSW plays the API.
setupMsw()

const code = 'LOG202609110007'

const item = (notificationId: number, overrides: Partial<NotificationItem> = {}): NotificationItem => ({
  notificationId, kind: 'TransferRequested', createdAtUtc: '2026-10-08T10:00:00Z', readAtUtc: null, evidenceCode: code,
  transferId: 7, requestedBy: lucia, from: nuria, to: diego, reason: 'Peritaje externo', decisionNotes: null, ...overrides,
})

const detail: EvidenceDetail = {
  code, typeCode: 'LOG', description: 'Log del firewall', capturedAtUtc: '2026-09-11T07:00:00Z', registeredAtUtc: '2026-09-11T08:00:00Z',
  registeredBy: lucia, initialCustodian: nuria, currentCustodian: nuria, eventCount: 0, lastEventAtUtc: '2026-09-11T08:00:00Z',
  content: { sha256: 'ab'.repeat(32), byteLength: 10, mediaType: 'text/plain' },
  integrity: { status: 'Unverified', checkedAtUtc: null, checkedThroughSeq: null }, pendingTransfer: null, anomalies: [],
}

/** The API's notifications for Diego, as the real endpoints answer: newest first, read state, the unread count. */
function notificationsApi(items: NotificationItem[], pages: Record<string, NotificationPage> = {}) {
  const calls = { list: [] as (string | null)[], unread: 0, markOne: [] as number[], markAll: [] as unknown[] }
  const unreadCount = () => items.filter((i) => !i.readAtUtc).length
  server.use(
    http.get('*/api/v1/notifications', ({ request }) => {
      const cursor = new URL(request.url).searchParams.get('cursor')
      calls.list.push(cursor)
      if (cursor && !pages[cursor])
        return HttpResponse.json({ status: 400, errors: { cursor: ['That cursor is not one this API issued.'] } }, { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
      return HttpResponse.json(cursor ? pages[cursor] : { items, nextCursor: Object.keys(pages)[0] ?? null, unreadCount: unreadCount() })
    }),
    http.get('*/api/v1/notifications/unread-count', () => {
      calls.unread++
      return HttpResponse.json({ unreadCount: unreadCount() })
    }),
    http.post('*/api/v1/notifications/:id/read', ({ params }) => {
      const id = Number(params.id)
      calls.markOne.push(id)
      items = items.map((i) => (i.notificationId === id ? { ...i, readAtUtc: '2026-10-09T09:00:00Z' } : i))
      return new HttpResponse(null, { status: 204 })
    }),
    http.post('*/api/v1/notifications/read', async ({ request }) => {
      const body = (await request.json()) as { upToId: number }
      calls.markAll.push(body)
      items = items.map((i) => (i.notificationId <= body.upToId ? { ...i, readAtUtc: '2026-10-09T09:00:00Z' } : i))
      return new HttpResponse(null, { status: 204 })
    }),
  )
  return calls
}

function renderAt(url: string) {
  const router = createMemoryRouter(routes, { initialEntries: [url] })
  render(<RouterProvider router={router} />)
  return router
}

const bell = (count?: number) => screen.findByRole('link', { name: count ? `Notificaciones, ${count} sin leer` : 'Notificaciones' })

describe('the notifications page', () => {
  it('words each item for the reader, newest first, and marks nothing read on opening', async () => {
    signInAs('Custodio')
    const calls = notificationsApi([
      item(12, { kind: 'TransferRejected', requestedBy: diego, from: nuria, to: nuria, decisionNotes: 'Falta el acta' }),
      item(9, { readAtUtc: '2026-10-08T11:00:00Z' }),
    ])
    renderAt('/notifications')

    await screen.findByRole('heading', { level: 1, name: 'Notificaciones' })
    const [newest, older] = within(screen.getByRole('list')).getAllByRole('listitem')
    expect(within(newest).getByRole('link')).toHaveAccessibleName('No leída. Nuria Paredes rechazó tu solicitud de transferir LOG202609110007. Motivo: Falta el acta')
    expect(within(newest).getByRole('link')).toHaveAttribute('href', `/evidence/${code}`)
    expect(within(older).getByRole('link')).toHaveAccessibleName('Lucía Ferrer solicitó transferirte LOG202609110007, ahora en custodia de Nuria Paredes.')
    expect(within(older).getByText('Peritaje externo')).toBeInTheDocument()
    expect(await bell(1)).toBeInTheDocument()
    expect(calls.markOne).toEqual([])
    expect(calls.markAll).toEqual([])
  })

  it('marks an item read when it is opened, and the evidence page loads once', async () => {
    signInAs('Custodio')
    const calls = notificationsApi([item(9)])
    let detailReads = 0
    server.use(
      http.get('/api/v1/people', () => HttpResponse.json([])),
      http.get(`/api/v1/evidence/${code}`, () => {
        detailReads++
        return HttpResponse.json(detail)
      }),
      http.get(`/api/v1/evidence/${code}/chain`, () => HttpResponse.json({ code, events: [] })),
    )
    const router = renderAt('/notifications')
    expect(await bell(1)).toBeInTheDocument()

    await userEvent.click(await screen.findByRole('link', { name: /solicitó transferirte/ }))

    await screen.findByRole('heading', { level: 1, name: code })
    expect(router.state.location.pathname).toBe(`/evidence/${code}`)
    expect(calls.markOne).toEqual([9])
    expect(await bell()).toBeInTheDocument()
    expect(detailReads).toBe(1)
  })

  it('marks read up to the newest item shown, so one that arrives meanwhile stays unread', async () => {
    signInAs('Custodio')
    const items = [item(9), item(8)]
    const calls = notificationsApi(items)
    renderAt('/notifications')
    await bell(2)
    // Another tab's request lands after this page loaded.
    calls.list.length = 0
    server.use(http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ unreadCount: calls.markAll.length ? 1 : 3 })))

    await userEvent.click(screen.getByRole('button', { name: 'Marcar todas como leídas' }))

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Marcar todas como leídas' })).not.toBeInTheDocument())
    expect(calls.markAll).toEqual([{ upToId: 9 }])
    expect(calls.list).toEqual([null]) // the page read itself again
    expect(screen.queryByText('No leída.', { exact: false })).not.toBeInTheDocument()
    expect(await bell(1)).toBeInTheDocument()
  })

  it('pages to older items and back, and sends a refused cursor to the first page', async () => {
    signInAs('Custodio')
    notificationsApi([item(9)], { AQAAAAAAAAAJ: { items: [item(3, { readAtUtc: '2026-10-01T00:00:00Z', reason: 'Copia forense' })], nextCursor: null, unreadCount: 1 } })
    const router = renderAt('/notifications')

    await userEvent.click(await screen.findByRole('link', { name: /Ver anteriores/ }))
    expect(await screen.findByText('Copia forense')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?cursor=AQAAAAAAAAAJ')
    expect(screen.getByRole('heading', { level: 1, name: 'Notificaciones' })).toHaveFocus()
    expect(screen.queryByRole('link', { name: /Ver anteriores/ })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('link', { name: 'Más recientes' }))
    expect(await screen.findByText('Peritaje externo')).toBeInTheDocument()

    await act(() => router.navigate('/notifications?cursor=forged'))
    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(screen.getByText('Peritaje externo')).toBeInTheDocument()
  })

  it('says so when there is nothing to read', async () => {
    signInAs('Supervisor')
    notificationsApi([])
    renderAt('/notifications')

    expect(await screen.findByText('No tienes notificaciones.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Marcar todas como leídas' })).not.toBeInTheDocument()
  })
})

describe('the bell', () => {
  it('asks again every 30 s, keeps its count through a failed poll, and stops while the tab is hidden', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    signInAs('Custodio')
    let answer = () => HttpResponse.json({ unreadCount: 3 })
    let polls = 0
    server.use(
      http.get('*/api/v1/evidence', () => HttpResponse.json({ items: [], nextCursor: null, prevCursor: null })),
      http.get('/api/v1/people', () => HttpResponse.json([])),
      http.get('*/api/v1/notifications/unread-count', () => {
        polls++
        return answer()
      }),
    )
    renderAt('/')
    expect(await bell(3)).toHaveAttribute('href', '/notifications')
    expect(polls).toBe(1)

    answer = () => HttpResponse.json({ status: 503 }, { status: 503 })
    await act(async () => vi.advanceTimersByTime(30_000))
    await waitFor(() => expect(polls).toBe(2))
    expect(await bell(3)).toBeInTheDocument()

    answer = () => HttpResponse.json({ unreadCount: 4 })
    await act(async () => vi.advanceTimersByTime(30_000))
    expect(await bell(4)).toBeInTheDocument()

    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
    await act(async () => vi.advanceTimersByTime(30_000))
    await waitFor(() => expect(polls).toBe(4))
    await act(async () => vi.advanceTimersByTime(120_000))
    expect(polls).toBe(4)

    visibility.mockReturnValue('visible')
    answer = () => HttpResponse.json({ unreadCount: 1 })
    act(() => document.dispatchEvent(new Event('visibilitychange')))
    expect(await bell(1)).toBeInTheDocument()
    expect(polls).toBe(5)
  })

  it('sends a refused token to sign in, and back to the page the user was on', async () => {
    signInAs('Custodio')
    notificationsApi([item(9)])
    server.use(http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ status: 401 }, { status: 401, headers: { 'Content-Type': 'application/problem+json' } })))
    const router = renderAt('/notifications')

    await screen.findByRole('heading', { name: 'Iniciar sesión' })
    expect(router.state.location.search).toBe(`?redirectTo=${encodeURIComponent('/notifications')}`)
  })
})
