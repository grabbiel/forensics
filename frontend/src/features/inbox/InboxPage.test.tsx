import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it, vi } from 'vitest'
import type { EvidenceSummary } from '../../api/evidence'
import { routes } from '../../routes'

const rows: EvidenceSummary[] = [
  { code: 'EML202610070001', typeCode: 'EML', registeredOn: '2026-10-07', description: 'Correo con adjunto sospechoso' },
  { code: 'LOG202610060001', typeCode: 'LOG', registeredOn: '2026-10-06', description: 'Log del firewall perimetral' },
]

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
  it('lists evidence newest first with type tags, dates and an announced count', async () => {
    stubFetch(200, rows)
    renderAt()

    const table = await screen.findByRole('table')
    const bodyRows = within(table).getAllByRole('row').slice(1)
    expect(bodyRows.map((row) => within(row).getByRole('rowheader').textContent)).toEqual(['EML202610070001', 'LOG202610060001'])
    expect(within(bodyRows[0]).getByText('(Correo electrónico)')).toBeInTheDocument()
    expect(within(bodyRows[0]).getByText('07 oct 2026')).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('2 evidencias')
  })

  it('sends the URL filters to the API and reflects them in the tabs and search', async () => {
    const fetchMock = stubFetch(200, [rows[1]])
    renderAt('/?q=firewall&type=LOG')

    await screen.findByRole('table')
    expect(String(fetchMock.mock.calls[0][0])).toBe('/api/v1/evidence?q=firewall&type=LOG')
    expect(screen.getByRole('button', { name: 'LOG' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('searchbox')).toHaveValue('firewall')
    expect(screen.getByRole('status')).toHaveTextContent('1 evidencia de tipo LOG para «firewall»')
  })

  it('marks a clicked tab at once, before the slow fetch returns', async () => {
    let release: (response: Response) => void = () => {}
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, rows))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementationOnce(() => new Promise<Response>((resolve) => (release = resolve)))

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))

    expect(screen.getByRole('button', { name: 'CSV' })).toHaveAttribute('aria-pressed', 'true')
    expect(String(fetchMock.mock.lastCall?.[0])).toBe('/api/v1/evidence?type=CSV')
    release(reply(200, []))
    await waitFor(() => expect(router.state.location.search).toBe('?type=CSV'))
  })

  it('keeps a pending tab choice when a search is submitted before the fetch returns', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, rows))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementationOnce(() => new Promise<Response>(() => {})) // the LOG load never settles

    await userEvent.click(screen.getByRole('button', { name: 'LOG' }))
    await userEvent.type(screen.getByRole('searchbox'), 'vpn{Enter}')

    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG&q=vpn'))
  })

  it('explains an empty result and clears the filters, moving focus to the heading', async () => {
    stubFetch(200, [])
    const router = renderAt('/?q=zzz')

    expect(await screen.findByText('Sin resultados para «zzz».')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Quitar filtros' }))

    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(screen.getByRole('heading', { name: 'Bandeja de evidencias' })).toHaveFocus()
  })

  it('shows the problem title from a failed request and recovers on retry', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => reply(503, { title: 'Servicio no disponible', status: 503 }, 'application/problem+json'))
    vi.stubGlobal('fetch', fetchMock)
    renderAt()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Servicio no disponible')
    fetchMock.mockImplementation(async () => reply(200, rows))
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('table')).toBeInTheDocument()
  })

  it('reports a network failure in plain words', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch'))))
    renderAt()

    expect(await screen.findByRole('alert')).toHaveTextContent('Sin conexión con el servidor')
  })

  it('renders unknown URLs inside the app frame', async () => {
    stubFetch(200, rows)
    renderAt('/no/existe')

    expect(await screen.findByRole('heading', { name: 'Página no encontrada' })).toBeInTheDocument()
    expect(screen.getByRole('searchbox')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Ir a la bandeja' })).toHaveAttribute('href', '/')
  })
})
