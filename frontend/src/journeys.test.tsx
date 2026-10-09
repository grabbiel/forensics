import { act, render, screen, waitFor, within, type RenderResult } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it } from 'vitest'
import type { ChainEvent, EvidenceDetail, EvidenceSummary, TransferView } from './api/evidence'
import { forgetPeople } from './api/people'
import { clearAllIntents } from './features/transfers/pendingIntent'
import { routes } from './routes'
import { custodians, diego, lucia, nuria, requestTo, UUID_V7 } from './test/fixtures'
import { server, setupMsw } from './test/msw'
import { signInAs } from './test/session'

// The brief's frontend cases, through the real router, fetch and API client; MSW stands in for the API, answering
// as the real one does (headers it requires, bodies and headers it sends).
setupMsw()

const code = 'LOG202609110007'

const summary = (code: string, description: string): EvidenceSummary => ({
  code,
  typeCode: code.slice(0, 3) as EvidenceSummary['typeCode'],
  description,
  currentCustodian: diego,
  lastEventAtUtc: '2026-10-07T14:03:00Z',
  eventCount: 1,
  integrityStatus: 'Unverified',
  integrityCheckedAtUtc: null,
  pendingTransfer: null,
})

const detail: EvidenceDetail = {
  code, typeCode: 'LOG', description: 'Log del firewall', capturedAtUtc: '2026-09-11T07:00:00Z', registeredAtUtc: '2026-09-11T08:00:00Z',
  registeredBy: lucia, initialCustodian: nuria, currentCustodian: nuria, eventCount: 1, lastEventAtUtc: '2026-09-11T08:00:00Z',
  content: { sha256: 'ab'.repeat(32), byteLength: 10, mediaType: 'text/plain' },
  integrity: { status: 'Unverified', checkedAtUtc: null, checkedThroughSeq: null }, pendingTransfer: null, anomalies: [],
}

const registered: ChainEvent = {
  eventId: 11, seq: 1, kind: 'EvidenceRegistered', occurredAtUtc: '2026-09-11T08:00:00Z', actor: lucia, from: null, to: nuria,
  transferId: null, notes: 'Registro inicial', keyId: 'dev', mac: 'aa'.repeat(32), prevMac: null,
}

const pendingToDiego: TransferView = {
  transferId: 7, status: 'Pending', from: nuria, to: diego, requestedBy: lucia, requestedAtUtc: '2026-10-08T10:00:00Z',
  reason: 'Peritaje externo', etag: '"00000000000007d1"',
}

/** The API's view of one evidence; tests change `detail` to play another tab or the server's own writes. */
function evidenceApi(db: { detail: EvidenceDetail }) {
  const reads = { detail: 0 }
  server.use(
    http.get('/api/v1/people', () => HttpResponse.json(custodians)),
    http.get(`/api/v1/evidence/${code}`, () => {
      reads.detail++
      return HttpResponse.json(db.detail)
    }),
    http.get(`/api/v1/evidence/${code}/chain`, () => HttpResponse.json({ code, events: [registered] })),
  )
  return reads
}

const problem = (status: number, body: Record<string, unknown>, headers: Record<string, string> = {}) =>
  HttpResponse.json({ status, ...body }, { status, headers: { 'Content-Type': 'application/problem+json', ...headers } })

/** The API's own header checks, before any handler logic: a UUID Idempotency-Key always, If-Match on decisions. */
function refuseBadHeaders(request: Request, needsIfMatch: boolean) {
  if (needsIfMatch && !request.headers.get('If-Match'))
    return problem(428, { type: 'urn:evidence-chain:problem:precondition-required', title: 'If-Match required.' })
  const key = request.headers.get('Idempotency-Key') ?? ''
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(key))
    return problem(400, { title: 'One or more validation errors occurred.', errors: { 'Idempotency-Key': ['Send one Idempotency-Key header with a new UUID for each distinct write; repeat it only to retry.'] } })
  return undefined
}

/** A transfer as the custody-transfer endpoints return it, with the headers they send. */
const transferResponse = (transfer: TransferView, status: number) =>
  HttpResponse.json(
    { ...transfer, evidenceCode: code, decidedBy: null, decidedAtUtc: null, decisionNotes: null },
    { status, headers: { ETag: transfer.etag, Location: `/api/v1/custody-transfers/${transfer.transferId}` } },
  )

function renderAt(url: string) {
  const router = createMemoryRouter(routes, { initialEntries: [url] })
  const view = render(<RouterProvider router={router} />)
  return { router, view }
}

async function openEvidence() {
  const rendered = renderAt(`/evidence/${code}`)
  await screen.findByRole('heading', { level: 1, name: code })
  return rendered
}

/** A reload: the page and this module's memory go, sessionStorage stays. */
function reload(view: RenderResult) {
  view.unmount()
  const stored = Object.entries(sessionStorage)
  clearAllIntents() // also empties the intents' in-memory copy, which a real reload would lose
  forgetPeople()
  for (const [name, value] of stored) sessionStorage.setItem(name, value)
}

const shownCodes = () => within(screen.getByRole('table')).queryAllByRole('rowheader').map((cell) => cell.textContent)
const currentCustodian = () => within(screen.getByText('Custodio actual').parentElement!).getByRole('definition')
const settle = (ms: number) => act(() => new Promise((resolve) => setTimeout(resolve, ms)))

describe('a slow answer for an earlier filter', () => {
  it('never replaces the results of the filter chosen after it', async () => {
    signInAs('Supervisor')
    let releaseLog: () => void = () => {}
    const logAnswer = new Promise<void>((resolve) => (releaseLog = resolve))
    const handled: string[] = []
    const rows: Record<string, EvidenceSummary[]> = {
      all: [summary('EML202610070001', 'Correo con adjunto'), summary('LOG202610060001', 'Log del firewall')],
      LOG: [summary('LOG202610060001', 'Log del firewall')],
      CSV: [summary('CSV202610050001', 'Exportación de accesos')],
    }
    server.use(
      http.get('/api/v1/people', () => HttpResponse.json(custodians)),
      http.get('/api/v1/evidence', async ({ request }) => {
        const type = new URL(request.url).searchParams.get('type') ?? 'all'
        if (type === 'LOG') {
          handled.push('LOG started')
          await logAnswer
        }
        handled.push(request.signal.aborted ? `${type} answered after the client gave up` : type)
        return HttpResponse.json({ items: rows[type], nextCursor: null })
      }),
    )
    const { router } = renderAt('/')
    await screen.findByRole('table')

    await userEvent.click(screen.getByRole('button', { name: 'LOG' })) // A: slow
    await waitFor(() => expect(handled).toContain('LOG started')) // A is in flight
    await userEvent.click(screen.getByRole('button', { name: 'CSV' })) // B: fast
    await waitFor(() => expect(shownCodes()).toEqual(['CSV202610050001']))

    await act(async () => releaseLog())
    await waitFor(() => expect(handled).toEqual(['all', 'LOG started', 'CSV', 'LOG answered after the client gave up']))
    await settle(100) // room for a late render of A's rows

    expect(shownCodes()).toEqual(['CSV202610050001'])
    expect(router.state.location.search).toBe('?type=CSV')
    expect(screen.getByRole('button', { name: 'CSV' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('status')).toHaveTextContent('1 evidencia de tipo CSV')
  })
})

describe('a decision the server refuses with 409', () => {
  it('never shows the decision as made, then shows the server’s state and explains it in a focused alert', async () => {
    signInAs('Custodio') // Diego Salas, the recipient
    const db: { detail: EvidenceDetail } = { detail: { ...detail, pendingTransfer: pendingToDiego } }
    evidenceApi(db)
    let ifMatch: string | null = null
    let release409: () => void = () => {}
    const answered = new Promise<void>((resolve) => (release409 = resolve))
    server.use(
      http.post('/api/v1/custody-transfers/7/accept', async ({ request }) => {
        const refused = refuseBadHeaders(request, true)
        if (refused) return refused
        ifMatch = request.headers.get('If-Match')
        await answered
        return problem(
          409,
          {
            type: 'urn:evidence-chain:problem:invalid-transition', title: "The transfer's state does not allow this.",
            detail: 'Transfer 7 is Rejected; Accept is not allowed.',
            currentState: {
              transferId: 7, evidenceId: 1, status: 'Rejected', fromCustodianId: 5, toCustodianId: 4, requestedById: 1,
              requestedAtUtc: '2026-10-08T10:00:00Z', decidedById: 4, decidedAtUtc: '2026-10-09T09:00:00Z',
            },
            currentETag: '"00000000000007d2"',
            actedBy: { ...diego, userName: 'custodio.demo', role: 'Custodio' },
            actedAtUtc: '2026-10-09T09:00:00Z',
          },
          { ETag: '"00000000000007d2"' },
        )
      }),
    )
    await openEvidence()
    // Meanwhile, in another tab, the same user rejected it.
    db.detail = { ...detail, pendingTransfer: null }

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    // While the accept is out: still pending, still Nuria's.
    await waitFor(() => expect(ifMatch).toBe(pendingToDiego.etag))
    expect(screen.getByText(/^Pendiente/)).toBeInTheDocument()
    expect(screen.queryByText(/Aceptada|Aceptaste/)).not.toBeInTheDocument()
    expect(currentCustodian()).toHaveTextContent('Nuria Paredes')

    await act(async () => release409())

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo aceptar: ya la rechazaste el 09 oct 2026, 09:00 UTC (quizá en otra pestaña).')
    expect(alert).not.toHaveTextContent('does not allow') // never the API's English
    await waitFor(() => expect(alert).toHaveFocus())
    expect(screen.getByText('Rechazada por Diego Salas')).toBeInTheDocument()
    expect(screen.queryByText(/Aceptada|Aceptaste/)).not.toBeInTheDocument()
    expect(currentCustodian()).toHaveTextContent('Nuria Paredes')
    expect(screen.queryByRole('button', { name: 'Aceptar custodia' })).not.toBeInTheDocument()
  })
})

describe('a request that got no answer', () => {
  /** The transfer endpoint: the first call never reaches the server, later ones create the transfer once. */
  function flakyCreate(db: { detail: EvidenceDetail }) {
    const keys: string[] = []
    let created: TransferView | undefined
    server.use(
      http.post('/api/v1/custody-transfers', async ({ request }) => {
        const refused = refuseBadHeaders(request, false)
        if (refused) return refused
        keys.push(request.headers.get('Idempotency-Key')!)
        if (keys.length === 1) return HttpResponse.error()
        if (created) return transferResponse(created, 201) // a replay; the real API also sends Idempotent-Replayed
        const body = (await request.json()) as { toCustodianId: number; reason: string }
        created = { ...pendingToDiego, to: body.toCustodianId === 4 ? diego : nuria, reason: body.reason }
        db.detail = { ...detail, pendingTransfer: created }
        return transferResponse(created, 201)
      }),
    )
    return keys
  }

  it('checks the evidence again and retries with the same Idempotency-Key', async () => {
    signInAs('Investigador')
    const db = { detail }
    const reads = evidenceApi(db)
    const keys = flakyCreate(db)
    await openEvidence()

    await requestTo('Diego Salas', 'Peritaje externo')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No hubo respuesta del servidor y la solicitud no aparece en la cadena.')
    expect(reads.detail).toBe(2) // the page read itself again before saying so
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    expect(keys).toHaveLength(2)
    expect(keys[0]).toMatch(UUID_V7)
    expect(keys[1]).toBe(keys[0])
  })

  it('keeps the key across a reload, so asking again for the same transfer resends it', async () => {
    signInAs('Investigador')
    const db = { detail }
    evidenceApi(db)
    const keys = flakyCreate(db)
    const { view } = await openEvidence()
    await requestTo('Diego Salas', 'Peritaje externo')
    await screen.findByRole('alert')

    reload(view)
    await openEvidence()
    await requestTo('Diego Salas', 'Peritaje externo')

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    expect(keys[1]).toBe(keys[0])
  })
})

describe('a request the server throttles', () => {
  it('says nothing was saved, re-reads nothing, holds every write, and retries with the same Idempotency-Key once the wait is over', async () => {
    signInAs('Investigador')
    const db = { detail }
    const reads = evidenceApi(db)
    const keys: string[] = []
    server.use(
      http.post('/api/v1/custody-transfers', ({ request }) => {
        const refused = refuseBadHeaders(request, false)
        if (refused) return refused
        keys.push(request.headers.get('Idempotency-Key')!)
        // The limiter answers before the endpoint runs; the real API then creates the transfer on the retry.
        if (keys.length === 1)
          return problem(429, { type: 'urn:evidence-chain:problem:rate-limited', title: 'Too many requests' }, { 'Retry-After': '1' })
        db.detail = { ...detail, pendingTransfer: pendingToDiego }
        return transferResponse(pendingToDiego, 201)
      }),
    )
    await openEvidence()

    await requestTo('Diego Salas', 'Peritaje externo')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo solicitar la transferencia: el servidor recibió demasiadas operaciones seguidas y no guardó nada. Espera 1 s antes de reintentar.')
    expect(alert).toHaveFocus()
    expect(reads.detail).toBe(1)
    const retry = within(alert).getByRole('button', { name: 'Reintentar' })
    const requestButton = screen.getByRole('button', { name: 'Solicitar transferencia' })
    expect(retry).toHaveAttribute('aria-disabled', 'true')
    expect(requestButton).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(retry)
    await userEvent.click(requestButton)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(keys).toHaveLength(1)

    await waitFor(() => expect(retry).toHaveAttribute('aria-disabled', 'false'), { timeout: 2_000 })
    expect(alert).toHaveTextContent('Ya puedes reintentar.')
    // Reopening the dialog offers the same request, saying it was not saved rather than that it is unconfirmed.
    await userEvent.click(requestButton)
    expect(await screen.findByRole('dialog')).toHaveTextContent('Tu última solicitud no se guardó')
    await userEvent.keyboard('{Escape}')
    await userEvent.click(retry)

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    expect(keys).toHaveLength(2)
    expect(keys[0]).toMatch(UUID_V7)
    expect(keys[1]).toBe(keys[0])
  })
})

describe('the transfer dialogs from the keyboard', () => {
  /** The page behind is hidden, and Tab, through more stops than the dialog has, never leaves it. */
  async function expectModal(dialog: HTMLElement) {
    expect(screen.queryByRole('heading', { level: 1, name: code })).not.toBeInTheDocument()
    for (let i = 0; i < 8; i++) {
      await userEvent.tab()
      expect(dialog).toContainElement(document.activeElement as HTMLElement)
    }
  }

  it('open on Enter with focus on the first field, keep focus inside, close on Escape and return it to the opener', async () => {
    signInAs('Investigador')
    evidenceApi({ detail })
    await openEvidence()
    const opener = screen.getByRole('button', { name: 'Solicitar transferencia' })

    opener.focus()
    await userEvent.keyboard('{Enter}')
    const dialog = await screen.findByRole('dialog', { name: 'Solicitar transferencia' })
    expect(dialog).toHaveAccessibleDescription('La custodia pasará a quien elijas cuando lo acepte. Mientras tanto queda pendiente.')
    expect(within(dialog).getByLabelText('Custodio que la recibirá')).toHaveFocus()
    await expectModal(dialog)

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(opener).toHaveFocus()
  })

  it('do the same for the recipient’s rejection', async () => {
    signInAs('Custodio')
    evidenceApi({ detail: { ...detail, pendingTransfer: pendingToDiego } })
    await openEvidence()
    const opener = screen.getByRole('button', { name: 'Rechazar' })

    opener.focus()
    await userEvent.keyboard('{Enter}')
    const dialog = await screen.findByRole('dialog', { name: 'Rechazar la transferencia' })
    expect(within(dialog).getByLabelText('Motivo del rechazo')).toHaveFocus()
    await expectModal(dialog)

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(opener).toHaveFocus()
  })
})
