import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it, vi } from 'vitest'
import type { EvidenceChain, EvidenceDetail } from './api/evidence'
import { safeRedirect } from './auth/guard'
import { getSession } from './auth/session'
import { routes } from './routes'
import { DEMO, signInAs } from './test/session'

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

  it('only follows same-site paths after sign-in', () => {
    expect(safeRedirect('/evidence/LOG1?x=1')).toBe('/evidence/LOG1?x=1')
    for (const target of [null, '', 'https://evil.example', '//evil.example', '/\\evil.example']) expect(safeRedirect(target)).toBe('/')
  })
})
