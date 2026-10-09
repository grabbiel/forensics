import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { EvidenceChain, EvidenceDetail, TransferView } from '../../api/evidence'
import type { Role } from '../../auth/session'
import { routes } from '../../routes'
import { DEMO, signInAs } from '../../test/session'
import { saveSession } from '../../auth/session'
import { clearIntent } from './pendingIntent'

const person = (id: number, displayName: string) => ({ id, displayName })
const code = 'LOG202609110007'
const custodians = [
  { id: 4, displayName: 'Diego Salas', role: 'Custodio' },
  { id: 5, displayName: 'Nuria Paredes', role: 'Custodio' },
]
const pending: TransferView = {
  transferId: 7, status: 'Pending', from: person(4, 'Diego Salas'), to: person(5, 'Nuria Paredes'), requestedBy: person(1, 'Lucía Ferrer'),
  requestedAtUtc: '2026-10-08T10:00:00Z', reason: 'Peritaje externo', etag: '"00000000000007d1"',
}
const base: EvidenceDetail = {
  code, typeCode: 'LOG', description: 'Log del firewall', capturedAtUtc: '2026-09-11T07:00:00Z', registeredAtUtc: '2026-09-11T08:00:00Z',
  registeredBy: person(1, 'Lucía Ferrer'), initialCustodian: person(4, 'Diego Salas'), currentCustodian: person(4, 'Diego Salas'),
  eventCount: 1, lastEventAtUtc: '2026-09-11T08:00:00Z', content: { sha256: 'ab'.repeat(32), byteLength: 10, mediaType: 'text/plain' },
  integrity: { status: 'Unverified', checkedAtUtc: null, checkedThroughSeq: null }, pendingTransfer: null, anomalies: [],
}
const chain: EvidenceChain = { code, events: [] }

function json(status: number, body: unknown, contentType = 'application/json', headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType, ...headers } })
}

/** The API as these tests need it: `state.detail` is what reads return; `write` answers the transfer endpoints. */
function stubApi(state: { detail: EvidenceDetail }, write: (url: string, init: RequestInit) => Promise<Response>) {
  const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
    const url = String(input)
    if (url === `/api/v1/evidence/${code}`) return json(200, state.detail)
    if (url === `/api/v1/evidence/${code}/chain`) return json(200, chain)
    if (url.startsWith('/api/v1/people')) return json(200, custodians)
    if (url.startsWith('/api/v1/custody-transfers')) return write(url, init!)
    return json(404, { status: 404 }, 'application/problem+json')
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const writes = (fetchMock: ReturnType<typeof stubApi>) => fetchMock.mock.calls.filter(([url]) => String(url).startsWith('/api/v1/custody-transfers'))
const header = (init: RequestInit | undefined, name: string) => ((init?.headers ?? {}) as Record<string, string>)[name]

function signInAsUser(role: Role, user = DEMO[role]) {
  saveSession({ accessToken: `token-${user.id}`, expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), user })
}

async function open() {
  render(<RouterProvider router={createMemoryRouter(routes, { initialEntries: [`/evidence/${code}`] })} />)
  return screen.findByRole('heading', { level: 1, name: code })
}

describe('TransferPanel', () => {
  beforeEach(() => {
    clearIntent(`request:${code}`)
    clearIntent(`decision:${code}`)
  })

  it('requests from the dialog, shows "Sending" until the server’s pending transfer replaces it, and says so', async () => {
    signInAs('Investigador')
    const state: { detail: EvidenceDetail } = { detail: base }
    let answer: (response: Response) => void = () => {}
    const fetchMock = stubApi(state, () => new Promise((resolve) => (answer = resolve)))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
    const dialog = screen.getByRole('dialog', { name: 'Solicitar transferencia' })
    await userEvent.selectOptions(within(dialog).getByLabelText('Custodio que la recibirá'), 'Nuria Paredes')
    expect(within(dialog).queryByRole('option', { name: 'Diego Salas' })).not.toBeInTheDocument() // already holds it
    await userEvent.type(within(dialog).getByLabelText('Motivo'), 'Peritaje externo')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))

    expect(await screen.findByText('Enviando…')).toBeInTheDocument()
    expect(screen.queryByText('Pendiente')).not.toBeInTheDocument()
    const [[, init]] = writes(fetchMock)
    expect(JSON.parse(String(init?.body))).toEqual({ evidenceCode: code, toCustodianId: 5, reason: 'Peritaje externo' })
    expect(header(init, 'Idempotency-Key')).toMatch(/^[0-9a-f-]{8}-[0-9a-f-]{4}-7/)

    state.detail = { ...base, pendingTransfer: pending }
    await act(async () => answer(json(201, { ...pending, evidenceCode: code, decidedBy: null, decidedAtUtc: null, decisionNotes: null })))

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    const notice = screen.getByText('Solicitud enviada: queda pendiente de que Nuria Paredes la acepte.')
    await waitFor(() => expect(notice.closest('[tabindex="-1"]')).toHaveFocus())
    expect(screen.queryByRole('button', { name: 'Solicitar transferencia' })).not.toBeInTheDocument()
  })

  it('lets only the recipient decide, accepting with the loaded ETag and its own key', async () => {
    signInAsUser('Custodio', { id: 5, userName: 'nuria.paredes', displayName: 'Nuria Paredes', role: 'Custodio' })
    const state: { detail: EvidenceDetail } = { detail: { ...base, pendingTransfer: pending } }
    const fetchMock = stubApi(state, async () => {
      state.detail = { ...base, currentCustodian: person(5, 'Nuria Paredes') }
      return json(200, { ...pending, status: 'Accepted', evidenceCode: code, decidedBy: person(5, 'Nuria Paredes'), decidedAtUtc: '2026-10-09T09:00:00Z', decisionNotes: null, etag: '"00000000000007d2"' })
    })
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    expect(await screen.findByText(`Aceptaste la custodia de ${code}.`)).toBeInTheDocument()
    const [[url, init]] = writes(fetchMock)
    expect(String(url)).toBe('/api/v1/custody-transfers/7/accept')
    expect(header(init, 'If-Match')).toBe(pending.etag)
    expect(header(init, 'Idempotency-Key')).toMatch(/^[0-9a-f]{8}-/)
    expect(screen.getByText('No hay ninguna transferencia pendiente.')).toBeInTheDocument()
  })

  it('offers no decision to anyone but the recipient', async () => {
    signInAs('Custodio') // Diego Salas, the current holder, not the recipient
    stubApi({ detail: { ...base, pendingTransfer: pending } }, async () => json(500, {}))
    await open()

    expect(screen.getByText('Pendiente')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Aceptar custodia' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Solicitar transferencia' })).not.toBeInTheDocument()
  })

  it('on a 409 shows the server’s state on the card, says who acted and when, and moves focus to the alert', async () => {
    signInAsUser('Custodio', { id: 5, userName: 'nuria.paredes', displayName: 'Nuria Paredes', role: 'Custodio' })
    stubApi({ detail: { ...base, pendingTransfer: pending } }, async () =>
      json(409, {
        type: 'urn:evidence-chain:problem:invalid-transition', status: 409,
        currentState: { transferId: 7, evidenceId: 1, status: 'Accepted', fromCustodianId: 4, toCustodianId: 5 },
        currentETag: '"00000000000007d2"', actedBy: { id: 5, userName: 'nuria.paredes', displayName: 'Nuria Paredes', role: 'Custodio' }, actedAtUtc: '2026-10-09T09:00:00Z',
      }, 'application/problem+json'))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo aceptar: Nuria Paredes ya la aceptó el 09 oct 2026, 09:00 UTC.')
    await waitFor(() => expect(alert).toHaveFocus())
    expect(screen.getByText('Aceptada por Nuria Paredes')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Aceptar custodia' })).not.toBeInTheDocument()
  })

  it('needs a reason to reject, and sends it', async () => {
    signInAsUser('Custodio', { id: 5, userName: 'nuria.paredes', displayName: 'Nuria Paredes', role: 'Custodio' })
    const state: { detail: EvidenceDetail } = { detail: { ...base, pendingTransfer: pending } }
    const fetchMock = stubApi(state, async () => {
      state.detail = base
      return json(200, { ...pending, status: 'Rejected', evidenceCode: code, decidedBy: person(5, 'Nuria Paredes'), decidedAtUtc: '2026-10-09T09:00:00Z', decisionNotes: 'Sin orden', etag: '"02"' })
    })
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Rechazar' }))
    const dialog = screen.getByRole('dialog', { name: 'Rechazar la transferencia' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Rechazar transferencia' }))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Explica por qué la rechazas')
    expect(writes(fetchMock)).toHaveLength(0)

    await userEvent.type(within(dialog).getByLabelText('Motivo del rechazo'), 'Sin orden')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Rechazar transferencia' }))

    expect(await screen.findByText('Rechazaste la transferencia; la custodia sigue con Diego Salas.')).toBeInTheDocument()
    expect(JSON.parse(String(writes(fetchMock)[0][1]?.body))).toEqual({ reason: 'Sin orden' })
  })

  it('checks an unanswered request against the page and retries it with the same key', async () => {
    signInAs('Investigador')
    const state: { detail: EvidenceDetail } = { detail: base }
    const answers = [() => Promise.reject(new TypeError('Failed to fetch')), async () => {
      state.detail = { ...base, pendingTransfer: pending }
      return json(201, { ...pending, evidenceCode: code, decidedBy: null, decidedAtUtc: null, decisionNotes: null })
    }]
    const fetchMock = stubApi(state, () => answers.shift()!())
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.selectOptions(within(dialog).getByLabelText('Custodio que la recibirá'), 'Nuria Paredes')
    await userEvent.type(within(dialog).getByLabelText('Motivo'), 'Peritaje externo')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No hubo respuesta del servidor y la solicitud no aparece.')
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar con la misma clave' }))

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    const [first, second] = writes(fetchMock)
    expect(header(second[1], 'Idempotency-Key')).toBe(header(first[1], 'Idempotency-Key'))
    expect(second[1]?.body).toBe(first[1]?.body)
  })
})
