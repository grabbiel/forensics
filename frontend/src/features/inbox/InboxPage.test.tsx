import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { EvidenceSummary, InboxPage } from '../../api/evidence'
import { DEBOUNCE_MS } from '../../layout/SearchBox'
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

/** One inbox page, the last one unless a cursor is given. */
const page = (items: EvidenceSummary[], nextCursor: string | null = null): InboxPage => ({ items, nextCursor })

const custodians = [
  { id: 4, displayName: 'Diego Salas', role: 'Custodio' },
  { id: 5, displayName: 'Nuria Paredes', role: 'Custodio' },
]

/** A JSON (or problem+json) response. */
function reply(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

/** The custodian list the filter offers; every other call goes to the inbox stub. */
function withPeople(inbox: (input: RequestInfo | URL) => Promise<Response>) {
  return async (input: RequestInfo | URL) => (String(input).startsWith('/api/v1/people') ? reply(200, custodians) : inbox(input))
}

/** Stubs the inbox with one canned response and returns the mock to inspect its calls. */
function stubFetch(status: number, body: unknown, contentType?: string) {
  const fetchMock = vi.fn<typeof fetch>(withPeople(async () => reply(status, body, contentType)))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** The inbox calls only, in order. */
const inboxCalls = (fetchMock: ReturnType<typeof stubFetch>) =>
  fetchMock.mock.calls.map(([input]) => String(input)).filter((url) => url.startsWith('/api/v1/evidence'))

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
    expect(inboxCalls(fetchMock)).toEqual(['/api/v1/evidence?q=firewall&type=LOG'])
    expect(fetchMock.mock.calls[0][1]?.headers).toMatchObject({ Authorization: 'Bearer token-Investigador' })
    expect(screen.getByRole('button', { name: 'LOG' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('searchbox')).toHaveValue('firewall')
    expect(screen.getByRole('status')).toHaveTextContent('1 evidencia de tipo LOG para «firewall»')
  })

  it('marks a clicked tab at once, before the slow fetch returns', async () => {
    let release: (response: Response) => void = () => {}
    const fetchMock = vi.fn<typeof fetch>(withPeople(async () => reply(200, page(rows))))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementation(withPeople(() => new Promise<Response>((resolve) => (release = resolve))))

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))

    expect(screen.getByRole('button', { name: 'CSV' })).toHaveAttribute('aria-pressed', 'true')
    expect(inboxCalls(fetchMock).at(-1)).toBe('/api/v1/evidence?type=CSV')
    release(reply(200, page([])))
    await waitFor(() => expect(router.state.location.search).toBe('?type=CSV'))
  })

  it('keeps a pending tab choice when a search is submitted before the fetch returns', async () => {
    const fetchMock = vi.fn<typeof fetch>(withPeople(async () => reply(200, page(rows))))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementationOnce(withPeople(() => new Promise<Response>(() => {}))) // the LOG load never settles

    await userEvent.click(screen.getByRole('button', { name: 'LOG' }))
    await userEvent.type(screen.getByRole('searchbox'), 'vpn{Enter}')

    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG&q=vpn'))
  })

  it('explains an empty result and clears the filters but not the sort, moving focus to the heading', async () => {
    stubFetch(200, page([]))
    const router = renderAt('/?q=zzz&sort=lastEventAt%3Aasc&cursor=abc')

    expect(await screen.findByText('Sin resultados para «zzz».')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Quitar filtros' }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=lastEventAt%3Aasc'))
    expect(screen.getByRole('heading', { name: 'Bandeja de evidencias' })).toHaveFocus()
  })

  it('also drops a search typed in the header and not yet sent, even when none was applied', async () => {
    const fetchMock = stubFetch(200, page([]))
    const router = renderAt('/?type=LOG')
    const clear = await screen.findByRole('button', { name: 'Quitar filtros' })
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }) // only the search box's pause; React keeps its own

    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'vpn' } })
    fireEvent.click(clear) // before the search box's pause ends
    await act(() => vi.advanceTimersByTimeAsync(DEBOUNCE_MS * 2))

    expect(router.state.location.search).toBe('')
    expect(screen.getByRole('searchbox')).toHaveValue('')
    expect(inboxCalls(fetchMock)).toEqual(['/api/v1/evidence?type=LOG', '/api/v1/evidence'])
  })

  it('explains a failed request in Spanish and recovers on retry', async () => {
    const fetchMock = vi.fn<typeof fetch>(withPeople(async () => reply(503, { title: 'Servicio no disponible', status: 503 }, 'application/problem+json')))
    vi.stubGlobal('fetch', fetchMock)
    renderAt()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo cargar la información')
    fetchMock.mockImplementation(withPeople(async () => reply(200, page(rows))))
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('table')).toBeInTheDocument()
  })

  it('asks to wait after a 429, and offers the retry only once the wait is over', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const throttled = async () =>
      new Response(JSON.stringify({ status: 429, type: 'urn:evidence-chain:problem:rate-limited' }), {
        status: 429,
        headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '30' },
      })
    const fetchMock = vi.fn<typeof fetch>(withPeople(throttled))
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/?q=firewall')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Demasiadas solicitudes')
    expect(alert).toHaveTextContent('El servidor recibió demasiadas solicitudes seguidas. Espera 30 segundos antes de volver a intentarlo.')
    const retry = within(alert).getByRole('button', { name: 'Reintentar' })
    expect(retry).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(retry)
    expect(inboxCalls(fetchMock)).toHaveLength(1)
    // The search box sits outside the inbox's error boundary, so what was typed stays.
    expect(screen.getByRole('searchbox')).toHaveValue('firewall')

    fetchMock.mockImplementation(withPeople(async () => reply(200, page(rows))))
    await act(async () => vi.advanceTimersByTime(30_000))
    expect(retry).toHaveAttribute('aria-disabled', 'false')
    expect(screen.getByText('Ya puedes volver a intentarlo.')).toBeInTheDocument() // said politely, outside the alert
    await userEvent.click(retry)

    expect(await screen.findByRole('table')).toBeInTheDocument()
  })

  it.each([new TypeError('Failed to fetch'), new DOMException('timed out', 'TimeoutError')])('reports a network failure or a timeout in plain words (%s)', async (failure) => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(withPeople(async () => Promise.reject(failure))))
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

  it('sends every URL filter to the API and shows each in its control', async () => {
    const fetchMock = stubFetch(200, page([rows[1]]))
    renderAt('/?q=vpn&type=LOG&custodianId=5&status=Invalid&sort=lastEventAt%3Aasc&cursor=abc')

    await screen.findByRole('table')
    expect(inboxCalls(fetchMock)).toEqual(['/api/v1/evidence?q=vpn&type=LOG&custodianId=5&status=Invalid&sort=lastEventAt%3Aasc&cursor=abc'])
    expect(screen.getByRole('combobox', { name: 'Custodio' })).toHaveValue('5')
    expect(screen.getByRole('combobox', { name: 'Integridad' })).toHaveValue('Invalid')
    expect(screen.getByRole('columnheader', { name: /Último evento/ })).toHaveAttribute('aria-sort', 'ascending')
    expect(screen.getByRole('status')).toHaveTextContent('1 evidencia de tipo LOG en custodia de Nuria Paredes con integridad «Alterada» para «vpn»')
  })

  it('applies a select at once and starts again from the first page', async () => {
    stubFetch(200, page(rows))
    const router = renderAt('/?type=LOG&cursor=abc')
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Custodio' }), 'Nuria Paredes')
    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG&custodianId=5'))

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Integridad' }), 'Sin verificar')
    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG&custodianId=5&status=Unverified'))
  })

  it('flips the order from the column header, says it with aria-sort, and starts from the first page', async () => {
    const fetchMock = stubFetch(200, page(rows))
    const router = renderAt('/?cursor=abc')
    const header = await screen.findByRole('columnheader', { name: /Último evento/ })
    expect(header).toHaveAttribute('aria-sort', 'descending')

    await userEvent.click(within(header).getByRole('button', { name: /Último evento/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=lastEventAt%3Aasc'))
    expect(inboxCalls(fetchMock).at(-1)).toBe('/api/v1/evidence?sort=lastEventAt%3Aasc')
    expect(await screen.findByRole('button', { name: 'Más antiguos primero, cambiar el orden' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: /Último evento/ })).toHaveAttribute('aria-sort', 'ascending')
  })

  it('also sorts from the summary bar, the control phones show', async () => {
    stubFetch(200, page(rows))
    const router = renderAt('/?type=LOG')

    await userEvent.click(await screen.findByRole('button', { name: 'Más recientes primero, cambiar el orden' }))

    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG&sort=lastEventAt%3Aasc'))
    expect(await screen.findByRole('button', { name: 'Más antiguos primero, cambiar el orden' })).toBeInTheDocument()
  })

  it('pages forward with the cursor and back to the first page', async () => {
    const fetchMock = stubFetch(200, page(rows, 'next-1'))
    const router = renderAt('/?type=EML')
    await screen.findByRole('table')
    expect(screen.queryByRole('button', { name: 'Primera página' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Página siguiente' }))
    await waitFor(() => expect(router.state.location.search).toBe('?type=EML&cursor=next-1'))
    expect(inboxCalls(fetchMock).at(-1)).toBe('/api/v1/evidence?type=EML&cursor=next-1')
    expect(screen.getByRole('heading', { name: 'Bandeja de evidencias' })).toHaveFocus()

    await userEvent.click(await screen.findByRole('button', { name: 'Primera página' }))
    await waitFor(() => expect(router.state.location.search).toBe('?type=EML'))
  })

  it('says it is loading and holds the pager while a load runs, since its cursor belongs to the old listing', async () => {
    let release: (response: Response) => void = () => {}
    const fetchMock = vi.fn<typeof fetch>(withPeople(async () => reply(200, page(rows, 'next-1'))))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()
    await screen.findByRole('table')
    fetchMock.mockImplementation(withPeople(() => new Promise<Response>((resolve) => (release = resolve))))

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))
    expect(screen.getByRole('status')).toHaveTextContent('Cargando evidencias…')
    const next = screen.getByRole('button', { name: 'Página siguiente' })
    expect(next).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(next)

    release(reply(200, page([])))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('0 evidencias de tipo CSV'))
    expect(router.state.location.search).toBe('?type=CSV')
    expect(inboxCalls(fetchMock)).toEqual(['/api/v1/evidence', '/api/v1/evidence?type=CSV'])
  })

  it('drops a cursor the API refuses and shows the first page of the same listing', async () => {
    const refused = { title: 'One or more validation errors occurred.', status: 400, errors: { cursor: ['Not a cursor this API issued for these filters and sort; start again without it.'] } }
    const fetchMock = vi.fn<typeof fetch>(
      withPeople(async (input) => (String(input).includes('cursor=') ? reply(400, refused, 'application/problem+json') : reply(200, page(rows)))),
    )
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt('/?type=EML&cursor=stale')

    await screen.findByRole('table')
    expect(router.state.location.search).toBe('?type=EML')
    expect(inboxCalls(fetchMock)).toEqual(['/api/v1/evidence?type=EML&cursor=stale', '/api/v1/evidence?type=EML'])
  })

  it('names a custodian the list lacks instead of showing "Todos"', async () => {
    stubFetch(200, page([]))
    renderAt('/?custodianId=999')

    const select = await screen.findByRole('combobox', { name: 'Custodio' })
    expect(select).toHaveValue('999')
    expect(within(select).getByRole('option', { selected: true })).toHaveTextContent('Custodio desconocido (#999)')
  })

  it('reads the custodian list once per visit and still lists evidence when it fails', async () => {
    const fetchMock = vi.fn<typeof fetch>(async (input) =>
      String(input).startsWith('/api/v1/people') ? reply(500, { title: 'Error', status: 500 }, 'application/problem+json') : reply(200, page(rows)),
    )
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt()

    await screen.findByRole('table')
    expect(within(screen.getByRole('combobox', { name: 'Custodio' })).getAllByRole('option').map((o) => o.textContent)).toEqual(['Todos'])

    fetchMock.mockImplementation(withPeople(async () => reply(200, page(rows))))
    await userEvent.click(screen.getByRole('button', { name: 'LOG' }))
    await waitFor(() => expect(router.state.location.search).toBe('?type=LOG'))
    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))
    await waitFor(() => expect(router.state.location.search).toBe('?type=CSV'))

    // The failed read is retried once, then served from the cache.
    expect(fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/v1/people'))).toHaveLength(2)
    expect(await screen.findByRole('option', { name: 'Nuria Paredes' })).toBeInTheDocument()
  })

  it('names who a pending transfer is waiting on and shows integrity in words', async () => {
    const pendingRow = { ...rows[0], integrityStatus: 'Invalid' as const, pendingTransfer: { transferId: 9, toCustodianId: 5, sinceUtc: '2026-10-07T15:00:00Z' } }
    stubFetch(200, page([pendingRow, rows[1]]))
    renderAt()

    const bodyRows = within(await screen.findByRole('table')).getAllByRole('row').slice(1)
    expect(within(bodyRows[0]).getByText('Pendiente de pasar a Nuria Paredes')).toBeInTheDocument()
    expect(within(bodyRows[0]).getByText('Alterada')).toBeInTheDocument()
    expect(within(bodyRows[1]).getByText('Sin verificar')).toBeInTheDocument()
  })
})
