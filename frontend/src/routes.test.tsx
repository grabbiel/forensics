import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, useFetcher, useParams, type RouteObject } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it, vi } from 'vitest'
import type { EvidenceChain, EvidenceDetail } from './api/evidence'
import { safeRedirect } from './auth/guard'
import { getSession } from './auth/session'
import { routes } from './routes'
import { DEMO, signInAs } from './test/session'

const route = (path: string) => routes.find((r) => r.path === path)!
const evidenceRoute = routes.find((r) => r.path === '/')!.children!.find((r) => r.path === 'evidence/:id')!

/** The real verify, transfer, login and evidence-loader routes, with a page that drives their fetchers. */
function harness(): RouteObject[] {
  function Probe() {
    const { id } = useParams()
    const verify = useFetcher()
    const write = useFetcher()
    return (
      <>
        <button type="button" onClick={() => verify.load(`/evidence/${id}/verify`)}>
          verificar
        </button>
        <button type="button" onClick={() => write.submit({ toCustodianId: '5', reason: 'x', idempotencyKey: 'k' }, { method: 'post', action: `/evidence/${id}/transfer` })}>
          enviar
        </button>
        <output>{write.state === 'idle' && write.data ? (write.data as { outcome: string }).outcome : ''}</output>
      </>
    )
  }
  return [route('/login'), route('/evidence/:id/verify'), route('/evidence/:id/transfer'), { ...evidenceRoute, path: '/evidence/:id', element: <Probe />, errorElement: undefined }]
}

const person = (id: number, displayName: string) => ({ id, displayName })
const detail: EvidenceDetail = {
  code: 'LOG202609110007',
  typeCode: 'LOG',
  description: 'Log del firewall fw-edge-01',
  capturedAtUtc: '2026-09-11T07:00:00Z',
  registeredAtUtc: '2026-09-11T08:00:00Z',
  registeredBy: person(1, 'Lucía Ferrer'),
  initialCustodian: person(4, 'Diego Salas'),
  currentCustodian: person(4, 'Diego Salas'),
  eventCount: 1,
  lastEventAtUtc: '2026-09-11T08:00:00Z',
  content: { sha256: 'ab'.repeat(32), byteLength: 2048, mediaType: 'text/plain' },
  integrity: { status: 'Unverified', checkedAtUtc: null, checkedThroughSeq: null },
  pendingTransfer: null,
  anomalies: [],
}
const chain: EvidenceChain = {
  code: detail.code,
  events: [{ eventId: 1, seq: 1, kind: 'EvidenceRegistered', occurredAtUtc: detail.registeredAtUtc, actor: person(1, 'Lucía Ferrer'), from: null, to: person(4, 'Diego Salas'), transferId: null, notes: 'Registro', keyId: 'dev', mac: 'ff'.repeat(32), prevMac: null }],
}

function json(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

/** A fake API: sign-in for the demo users, and the one evidence. */
function fakeApi(overrides: (url: string, init?: RequestInit) => Response | undefined = () => undefined) {
  const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
    const url = String(input)
    const overridden = overrides(url, init)
    if (overridden) return overridden
    if (url === '/api/v1/auth/token') {
      const { userName } = JSON.parse(String(init?.body)) as { userName: string }
      const user = Object.values(DEMO).find((u) => u.userName === userName)
      return user
        ? json(200, { accessToken: `token-${user.role}`, tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), user })
        : json(400, { status: 400, errors: { userName: ['No demo user has that name.'] } }, 'application/problem+json')
    }
    if (url === `/api/v1/evidence/${detail.code}`) return json(200, detail)
    if (url === `/api/v1/evidence/${detail.code}/chain`) return json(200, chain)
    if (url === '/api/v1/evidence' || url.startsWith('/api/v1/evidence?')) return json(200, { items: [], nextCursor: null })
    if (url.startsWith('/api/v1/people')) return json(200, [])
    return json(404, { status: 404 }, 'application/problem+json')
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderAt(url: string) {
  const router = createMemoryRouter(routes, { initialEntries: [url] })
  render(<RouterProvider router={router} />)
  return router
}

describe('routes and sign-in', () => {
  it('sends a signed-out visitor to sign in, then back to where they were headed', async () => {
    fakeApi()
    const router = renderAt(`/evidence/${detail.code}`)

    await screen.findByRole('heading', { name: 'Iniciar sesión' })
    expect(router.state.location.search).toBe(`?redirectTo=${encodeURIComponent(`/evidence/${detail.code}`)}`)

    await userEvent.click(screen.getByRole('button', { name: /Diego Salas/ }))

    expect(await screen.findByRole('heading', { name: detail.code })).toBeInTheDocument()
    expect(getSession()?.user.role).toBe('Custodio')
    expect(screen.getByText('Diego Salas', { selector: '.account__name' })).toBeInTheDocument()
  })

  it('explains an unknown user without leaving the page', async () => {
    fakeApi()
    renderAt('/login')

    await userEvent.type(await screen.findByLabelText('Otra persona del equipo'), 'nadie{Enter}')

    expect(await screen.findByRole('alert')).toHaveTextContent('No hay ningún usuario «nadie».')
    expect(getSession()).toBeNull()
  })

  it('asks to wait after too many sign-ins, and holds every way to sign in until the wait is over', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let throttled = true
    const fetchMock = fakeApi((url) =>
      url === '/api/v1/auth/token' && throttled
        ? new Response(JSON.stringify({ status: 429 }), { status: 429, headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '30' } })
        : undefined,
    )
    const signIns = () => fetchMock.mock.calls.filter(([url]) => String(url) === '/api/v1/auth/token').length
    const router = renderAt('/login')

    const persona = await screen.findByRole('button', { name: /Lucía Ferrer/ })
    await userEvent.click(persona)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Demasiados intentos de inicio de sesión seguidos. Espera 30 segundos antes de volver a intentarlo.')
    expect(persona).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByRole('button', { name: 'Entrar' })).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(persona)
    await userEvent.type(screen.getByLabelText('Otra persona del equipo'), 'nuria.paredes{Enter}')
    expect(signIns()).toBe(1)

    throttled = false
    await act(async () => vi.advanceTimersByTime(30_000))
    expect(persona).toHaveAttribute('aria-disabled', 'false')
    expect(screen.getByText('Ya puedes volver a intentarlo.')).toBeInTheDocument() // said politely, outside the alert
    await userEvent.click(persona)

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
    expect(getSession()?.user.role).toBe('Investigador')
  })

  it('forgets a refused token and asks to sign in again, keeping the way back', async () => {
    signInAs('Supervisor')
    fakeApi((url) => (url.startsWith(`/api/v1/evidence/${detail.code}`) ? json(401, { status: 401 }, 'application/problem+json') : undefined))
    const router = renderAt(`/evidence/${detail.code}`)

    await screen.findByRole('heading', { name: 'Iniciar sesión' })
    expect(router.state.location.search).toContain('redirectTo=')
    expect(getSession()).toBeNull()
  })

  it('signs out from the header', async () => {
    signInAs('Investigador')
    fakeApi()
    const router = renderAt('/')

    await userEvent.click(await screen.findByRole('button', { name: 'Cerrar sesión de Lucía Ferrer' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(getSession()).toBeNull()
  })

  it('shows a missing evidence inside the app frame with a way back, not a retry', async () => {
    signInAs('Investigador')
    fakeApi()
    renderAt('/evidence/LOG209901010001')

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByRole('heading', { name: 'Evidencia no encontrada' })).toBeInTheDocument()
    expect(within(alert).queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument()
    expect(within(alert).getByRole('link', { name: 'Ir a la bandeja' })).toHaveAttribute('href', '/')
  })

  it('only follows pages of this site after sign-in', () => {
    expect(safeRedirect('/evidence/LOG1?x=1')).toBe('/evidence/LOG1?x=1')
    expect(safeRedirect('/?q=vpn')).toBe('/?q=vpn')
    const refused = [null, '', 'https://evil.example', '//evil.example', '/\\evil.example', '/login', '/logout', '/evidence/LOG1/verify', '/transfers/7/accept']
    for (const target of refused) expect(safeRedirect(target)).toBe('/')
  })

  it('never loops through /login, even when told to return there', async () => {
    signInAs('Investigador')
    fakeApi()
    const router = renderAt('/login?redirectTo=%2Flogin')

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
  })

  it('keeps the chosen persona busy, and does not sign in twice, until the next page has loaded', async () => {
    let release: () => void = () => {}
    const fetchMock = fakeApi()
    const inboxReady = new Promise<void>((resolve) => (release = resolve))
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input, init) => {
      if (String(input).startsWith('/api/v1/evidence')) await inboxReady
      return base(input, init)
    })
    renderAt('/login')

    const persona = await screen.findByRole('button', { name: /Diego Salas/ })
    await userEvent.click(persona)
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).startsWith('/api/v1/evidence'))).toBe(true))

    expect(persona).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByRole('button', { name: /Lucía Ferrer/ })).toBeDisabled()
    await userEvent.click(persona)
    expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/v1/auth/token')).toHaveLength(1)

    await act(async () => release())
    expect(await screen.findByRole('heading', { name: 'Bandeja de evidencias' })).toBeInTheDocument()
  })

  it('guards pages without data too, so a signed-out visitor never sees the app frame', async () => {
    fakeApi()
    const router = renderAt('/no/existe')

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })
})

describe('resource routes', () => {
  it('returns a fetcher whose token was refused to the page it was used from, not to the resource route', async () => {
    signInAs('Supervisor')
    fakeApi((url) => (url.endsWith('/chain/verify') ? json(401, { status: 401 }, 'application/problem+json') : undefined))
    const router = createMemoryRouter(harness(), { initialEntries: [`/evidence/${detail.code}`] })
    render(<RouterProvider router={router} />)

    await userEvent.click(await screen.findByRole('button', { name: 'verificar' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
    expect(router.state.location.search).toBe(`?redirectTo=${encodeURIComponent(`/evidence/${detail.code}`)}`)
  })

  it('re-reads the evidence after every answer to a write but a 429, never re-running a verification', async () => {
    signInAs('Investigador')
    const answers = [201, 409, 0, 422, 500, 429]
    const fetchMock = fakeApi((url) => {
      if (url.endsWith('/chain/verify')) return json(200, { code: detail.code, valid: true, verifiedThroughSeq: 1, eventCount: 1, checkedAtUtc: '2026-10-09T00:00:00Z', firstInvalid: null })
      if (url !== '/api/v1/custody-transfers') return undefined
      const status = answers.shift()!
      if (status === 0) throw new TypeError('Failed to fetch')
      return status === 201 ? json(201, { transferId: 9, status: 'Pending', etag: '"01"' }) : json(status, { status }, 'application/problem+json')
    })
    const calls = (suffix: string) => fetchMock.mock.calls.filter(([url]) => String(url) === `/api/v1/evidence/${detail.code}${suffix}`).length
    render(<RouterProvider router={createMemoryRouter(harness(), { initialEntries: [`/evidence/${detail.code}`] })} />)

    await userEvent.click(await screen.findByRole('button', { name: 'verificar' }))
    await waitFor(() => expect(calls('/chain/verify')).toBe(1))
    expect(calls('')).toBe(1)

    for (const [outcome, reads] of [['done', 2], ['refused', 3], ['unknown', 4], ['refused', 5], ['unknown', 6], ['throttled', 6]] as const) {
      await userEvent.click(screen.getByRole('button', { name: 'enviar' }))
      await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(outcome))
      await waitFor(() => expect(calls('')).toBe(reads))
      expect(calls('/chain/verify')).toBe(1)
      await act(async () => {}) // let any revalidation settle before the next write
    }
    expect(calls('')).toBe(6) // still, once the throttled write has settled
  })
})
