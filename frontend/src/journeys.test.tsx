import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it } from 'vitest'
import type { EvidenceDetail, EvidenceSummary, TransferView } from './api/evidence'
import { routes } from './routes'
import { server, setupMsw } from './test/msw'
import { signInAs } from './test/session'

// The brief's frontend cases, through the real router, fetch and API client; MSW stands in for the API.
setupMsw()

const person = (id: number, displayName: string) => ({ id, displayName })
const lucia = person(1, 'Lucía Ferrer')
const diego = person(4, 'Diego Salas')
const nuria = person(5, 'Nuria Paredes')
const custodians = [
  { ...diego, role: 'Custodio' },
  { ...nuria, role: 'Custodio' },
]
const code = 'LOG202609110007'
const UUID_V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

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
    http.get(`/api/v1/evidence/${code}/chain`, () => HttpResponse.json({ code, events: [] })),
  )
  return reads
}

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

/** Fills and sends the request dialog as an Investigador would. */
async function requestTo(name: string, reason: string) {
  await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
  const dialog = screen.getByRole('dialog', { name: 'Solicitar transferencia' })
  await userEvent.selectOptions(within(dialog).getByLabelText('Custodio que la recibirá'), name)
  await userEvent.clear(within(dialog).getByLabelText('Motivo'))
  await userEvent.type(within(dialog).getByLabelText('Motivo'), reason)
  await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))
}

const shownCodes = () => within(screen.getByRole('table')).queryAllByRole('rowheader').map((cell) => cell.textContent)

describe('a slow answer for an earlier filter', () => {
  it('never replaces the results of the filter chosen after it', async () => {
    signInAs('Supervisor')
    let releaseLog: () => void = () => {}
    const logAnswer = new Promise<void>((resolve) => (releaseLog = resolve))
    const answered: string[] = []
    const rows: Record<string, EvidenceSummary[]> = {
      all: [summary('EML202610070001', 'Correo con adjunto'), summary('LOG202610060001', 'Log del firewall')],
      LOG: [summary('LOG202610060001', 'Log del firewall')],
      CSV: [summary('CSV202610050001', 'Exportación de accesos')],
    }
    server.use(
      http.get('/api/v1/people', () => HttpResponse.json(custodians)),
      http.get('/api/v1/evidence', async ({ request }) => {
        const type = new URL(request.url).searchParams.get('type') ?? 'all'
        if (type === 'LOG') await logAnswer
        answered.push(type)
        return HttpResponse.json({ items: rows[type], nextCursor: null })
      }),
    )
    const { router } = renderAt('/')
    await screen.findByRole('table')

    await userEvent.click(screen.getByRole('button', { name: 'LOG' })) // A: slow
    await userEvent.click(screen.getByRole('button', { name: 'CSV' })) // B: fast
    await waitFor(() => expect(shownCodes()).toEqual(['CSV202610050001']))

    await act(async () => {
      releaseLog()
      await logAnswer
    })
    await waitFor(() => expect(answered).toEqual(['all', 'CSV', 'LOG']))

    expect(shownCodes()).toEqual(['CSV202610050001'])
    expect(router.state.location.search).toBe('?type=CSV')
    expect(screen.getByRole('button', { name: 'CSV' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('status')).toHaveTextContent('1 evidencia de tipo CSV')
  })
})

describe('a decision the server refuses with 409', () => {
  it('shows the server’s state, never the one we asked for, and explains it in a focused alert', async () => {
    signInAs('Custodio') // Diego Salas, the recipient
    const db: { detail: EvidenceDetail } = { detail: { ...detail, pendingTransfer: pendingToDiego } }
    evidenceApi(db)
    let ifMatch: string | null = null
    server.use(
      http.post('/api/v1/custody-transfers/7/accept', ({ request }) => {
        ifMatch = request.headers.get('If-Match')
        return HttpResponse.json(
          {
            type: 'urn:evidence-chain:problem:invalid-transition', title: 'La transferencia ya no admite esa acción.', status: 409,
            currentState: { transferId: 7, evidenceId: 1, status: 'Rejected', fromCustodianId: 5, toCustodianId: 4 },
            currentETag: '"00000000000007d2"', actedBy: diego, actedAtUtc: '2026-10-09T09:00:00Z',
          },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        )
      }),
    )
    await openEvidence()
    // Meanwhile, in another tab, the same user rejected it.
    db.detail = { ...detail, pendingTransfer: null }

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo aceptar: ya la rechazaste el 09 oct 2026, 09:00 UTC (quizá en otra pestaña).')
    await waitFor(() => expect(alert).toHaveFocus())
    expect(ifMatch).toBe(pendingToDiego.etag)
    expect(screen.getByText('Rechazada por Diego Salas')).toBeInTheDocument()
    expect(screen.queryByText(/Aceptada|Aceptaste/)).not.toBeInTheDocument()
    expect(screen.getByText('Custodio actual').nextElementSibling).toHaveTextContent('Nuria Paredes')
    expect(screen.queryByRole('button', { name: 'Aceptar custodia' })).not.toBeInTheDocument()
  })
})

describe('a request that got no answer', () => {
  /** The transfer endpoint: the first call never reaches the server, later ones create the transfer once. */
  function flakyCreate(db: { detail: EvidenceDetail }) {
    const keys: string[] = []
    server.use(
      http.post('/api/v1/custody-transfers', async ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key') ?? '')
        if (keys.length === 1) return HttpResponse.error()
        const body = (await request.json()) as { toCustodianId: number; reason: string }
        const transfer = { ...pendingToDiego, from: nuria, to: body.toCustodianId === 4 ? diego : nuria, reason: body.reason }
        db.detail = { ...detail, pendingTransfer: transfer }
        return HttpResponse.json({ ...transfer, evidenceCode: code, decidedBy: null, decidedAtUtc: null, decisionNotes: null }, { status: 201 })
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

    view.unmount() // a reload: only sessionStorage survives
    await openEvidence()
    await requestTo('Diego Salas', 'Peritaje externo')

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    expect(keys[1]).toBe(keys[0])
  })
})

describe('the transfer dialogs from the keyboard', () => {
  /** Tabs through more stops than the dialog has, checking focus never leaves it. */
  async function expectFocusTrappedIn(dialog: HTMLElement) {
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
    await expectFocusTrappedIn(dialog)

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
    await expectFocusTrappedIn(dialog)

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(opener).toHaveFocus()
  })
})
