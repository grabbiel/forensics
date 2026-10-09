import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { EvidenceSummary, InboxPage } from '../../api/evidence'
import { routes } from '../../routes'
import { signInAs } from '../../test/session'

const summary = (code: string, description: string, lastEventAtUtc: string, custodian: string): EvidenceSummary => ({
  code,
  typeCode: code.slice(0, 3) as EvidenceSummary['typeCode'],
  description,
  currentCustodian: { id: 4, displayName: custodian },
  lastEventAtUtc,
  eventCount: 3,
  integrityStatus: 'Unverified',
  integrityCheckedAtUtc: null,
  pendingTransfer: null,
})

const rows: EvidenceSummary[] = [
  summary('EML202610070001', 'Correo con adjunto sospechoso', '2026-10-07T14:03:00Z', 'Diego Salas'),
  summary('LOG202610060001', 'Log del firewall perimetral', '2026-10-06T09:30:00Z', 'Nuria Paredes'),
]

/** One inbox page, the last one. */
const page = (items: EvidenceSummary[]): InboxPage => ({ items, nextCursor: null })

/** A JSON (or problem+json) response. */
function reply(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

/** Stubs fetch with one canned response and returns the mock to inspect calls. */
function stubFetch(status: number, body: unknown, contentType?: string) {
  const fetchMock = vi.fn<typeof fetch>(async () => reply(status, body, contentType))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** Renders the real route table at the given URL. */
function renderAt(url = '/') {
  const router = createMemoryRouter(routes, { initialEntries: [url] })
  render(<RouterProvider router={router} />)
  return router
}

describe('InboxPage', () => {
  beforeEach(() => signInAs('Investigador'))

  it('lists evidence newest first with links, type tags, custodians, last events and an announced count', async () => {
    stubFetch(200, page(rows))
    renderAt()

    const table = await screen.findByRole('table')
    const bodyRows = within(table).getAllByRole('row').slice(1)
    expect(bodyRows.map((row) => within(row).getByRole('rowheader').textContent)).toEqual(['EML202610070001', 'LOG202610060001'])
    expect(within(bodyRows[0]).getByText('(Correo electrónico)')).toBeInTheDocument()
    expect(within(bodyRows[0]).getByRole('link', { name: 'EML202610070001' })).toHaveAttribute('href', '/evidence/EML202610070001')
    expect(within(bodyRows[0]).getByText('Diego Salas')).toBeInTheDocument()
    expect(within(bodyRows[0]).getByText('07 oct 2026, 14:03 UTC')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('2 evidencias')
  })

  it('sends the URL filters to the API and reflects them in the tabs and search', async () => {
    const fetchMock = stubFetch(200, page([rows[1]]))
    renderAt('/?q=firewall&type=LOG')

    await screen.findByRole('table')
    expect(String(fetchMock.mock.calls[0][0])).toBe('/api/v1/evidence?q=firewall&type=LOG')
    expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer token-Investigador' })
    expect(screen.getByRole('button', { name: 'LOG' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('searchbox')).toHaveValue('firewall')
    expect(screen.getByRole('status')).toHaveTextContent('1 evidencia de tipo LOG para «firewall»')
  })

  it('marks a clicked tab at once, before the slow fetch returns', async () => {
    let release: (response: Response) => void = () => {}
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, page(rows)))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementationOnce(() => new Promise<Response>((resolve) => (release = resolve)))

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))

    expect(screen.getByRole('button', { name: 'CSV' })).toHaveAttribute('aria-pressed', 'true')
    expect(String(fetchMock.mock.lastCall?.[0])).toBe('/api/v1/evidence?type=CSV')
    release(reply(200, page([])))
    await waitFor(() => expect(router.state.location.search).toBe('?type=CSV'))
  })

  it('keeps a pending tab choice when a search is submitted before the fetch returns', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, page(rows)))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementationOnce(() => new Promise<Response>(() => {})) // the LOG load never settles

    await userEvent.click(screen.getByRole('button', { name: 'LOG' }))
    await userEvent.type(screen.getByRole('searchbox'), 'vpn{Enter}')

    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG&q=vpn'))
  })

  it('explains an empty result and clears the filters, moving focus to the heading', async () => {
    stubFetch(200, page([]))
    const router = renderAt('/?q=zzz')

    expect(await screen.findByText('Sin resultados para «zzz».')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Quitar filtros' }))

    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(screen.getByRole('heading', { name: 'Bandeja de evidencias' })).toHaveFocus()
  })

  it('explains a failed request in Spanish and recovers on retry', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => reply(503, { title: 'Servicio no disponible', status: 503 }, 'application/problem+json'))
    vi.stubGlobal('fetch', fetchMock)
    renderAt()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo cargar la información')
    fetchMock.mockImplementation(async () => reply(200, page(rows)))
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('table')).toBeInTheDocument()
  })

  it('reports a network failure in plain words', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch'))))
    renderAt()

    expect(await screen.findByRole('alert')).toHaveTextContent('Sin conexión con el servidor')
  })

  it('renders unknown URLs inside the app frame', async () => {
    stubFetch(200, page(rows))
    renderAt('/no/existe')

    expect(await screen.findByRole('heading', { name: 'Página no encontrada' })).toBeInTheDocument()
    expect(screen.getByRole('searchbox')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a la bandeja' })).toHaveAttribute('href', '/')
  })
})
