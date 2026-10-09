import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it, vi } from 'vitest'
import type { ChainEvent, EvidenceDetail, TransferView } from '../../api/evidence'
import type { Role, SessionUser } from '../../auth/session'
import { saveSession } from '../../auth/session'
import { routes } from '../../routes'
import { DEMO, signInAs } from '../../test/session'
import { getIntent, intentScope, startIntent } from './pendingIntent'

const person = (id: number, displayName: string) => ({ id, displayName })
const lucia = person(1, 'Lucía Ferrer')
const diego = person(4, 'Diego Salas')
const nuria = person(5, 'Nuria Paredes')
const NURIA: SessionUser = { id: 5, userName: 'nuria.paredes', displayName: 'Nuria Paredes', role: 'Custodio' }
const UUID_V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/
const code = 'LOG202609110007'
const custodians = [
  { ...diego, role: 'Custodio' },
  { ...nuria, role: 'Custodio' },
]
const pending: TransferView = {
  transferId: 7, status: 'Pending', from: diego, to: nuria, requestedBy: lucia,
  requestedAtUtc: '2026-10-08T10:00:00Z', reason: 'Peritaje externo', etag: '"00000000000007d1"',
}
const base: EvidenceDetail = {
  code, typeCode: 'LOG', description: 'Log del firewall', capturedAtUtc: '2026-09-11T07:00:00Z', registeredAtUtc: '2026-09-11T08:00:00Z',
  registeredBy: lucia, initialCustodian: diego, currentCustodian: diego,
  eventCount: 1, lastEventAtUtc: '2026-09-11T08:00:00Z', content: { sha256: 'ab'.repeat(32), byteLength: 10, mediaType: 'text/plain' },
  integrity: { status: 'Unverified', checkedAtUtc: null, checkedThroughSeq: null }, pendingTransfer: null, anomalies: [],
}
const withPending: EvidenceDetail = { ...base, pendingTransfer: pending }

/** A custody event as the chain lists it, happening now unless said otherwise. */
function event(kind: ChainEvent['kind'], actor: { id: number; displayName: string }, extra: Partial<ChainEvent> = {}): ChainEvent {
  return {
    eventId: 20, seq: 2, kind, occurredAtUtc: new Date().toISOString(), actor, from: diego, to: nuria, transferId: 7,
    notes: '', keyId: 'dev', mac: 'cc'.repeat(32), prevMac: 'aa'.repeat(32), ...extra,
  }
}

const transferBody = (overrides: Partial<TransferView> & Record<string, unknown> = {}) => ({
  ...pending, evidenceCode: code, decidedBy: null, decidedAtUtc: null, decisionNotes: null, ...overrides,
})

function json(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

const problem = (status: number, body: Record<string, unknown> = {}) => json(status, { status, ...body }, 'application/problem+json')

/** What the API holds; tests change it to play the server's own writes or another tab. */
interface Server {
  detail: EvidenceDetail
  chain: ChainEvent[]
}

/** The API as these tests need it: reads come from `server`; `write` answers the transfer endpoints. */
function stubApi(server: Server, write: (url: string, init: RequestInit) => Promise<Response>) {
  const fetchMock = vi.fn<typeof fetch>(async (input, init) => {
    const url = String(input)
    if (url === `/api/v1/evidence/${code}`) return json(200, server.detail)
    if (url === `/api/v1/evidence/${code}/chain`) return json(200, { code, events: server.chain })
    if (url.startsWith('/api/v1/people')) return json(200, custodians)
    if (url.startsWith('/api/v1/custody-transfers')) return write(url, init!)
    return problem(404)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const writes = (fetchMock: ReturnType<typeof stubApi>) => fetchMock.mock.calls.filter(([url]) => String(url).startsWith('/api/v1/custody-transfers'))
const detailReads = (fetchMock: ReturnType<typeof stubApi>) => fetchMock.mock.calls.filter(([url]) => String(url) === `/api/v1/evidence/${code}`).length
const header = (init: RequestInit | undefined, name: string) => ((init?.headers ?? {}) as Record<string, string>)[name]

function signInAsUser(role: Role, user = DEMO[role]) {
  saveSession({ accessToken: `token-${user.id}`, expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), user })
}

async function open() {
  const view = render(<RouterProvider router={createMemoryRouter(routes, { initialEntries: [`/evidence/${code}`] })} />)
  await screen.findByRole('heading', { level: 1, name: code })
  return view
}

const progress = () => within(screen.getByRole('region', { name: 'Transferencia de custodia' })).getAllByRole('status')[0]

/** Fills and sends the request dialog. */
async function requestTo(name: string, reason: string) {
  await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
  const dialog = screen.getByRole('dialog', { name: 'Solicitar transferencia' })
  await userEvent.selectOptions(within(dialog).getByLabelText('Custodio que la recibirá'), name)
  await userEvent.clear(within(dialog).getByLabelText('Motivo'))
  await userEvent.type(within(dialog).getByLabelText('Motivo'), reason)
  await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))
}

describe('TransferPanel', () => {
  it('requests from the dialog, shows "Sending" until the server’s pending transfer replaces it, and says so', async () => {
    signInAs('Investigador')
    const server: Server = { detail: base, chain: [] }
    let answer: (response: Response) => void = () => {}
    const fetchMock = stubApi(server, () => new Promise((resolve) => (answer = resolve)))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
    expect(within(screen.getByRole('dialog')).queryByRole('option', { name: 'Diego Salas' })).not.toBeInTheDocument() // already holds it
    await userEvent.keyboard('{Escape}')
    await requestTo('Nuria Paredes', 'Peritaje externo')

    expect(await screen.findByText('Enviando…')).toBeInTheDocument()
    expect(progress()).toHaveTextContent('Enviando la solicitud…')
    expect(screen.queryByText('Pendiente')).not.toBeInTheDocument()
    const [[, init]] = writes(fetchMock)
    expect(JSON.parse(String(init?.body))).toEqual({ evidenceCode: code, toCustodianId: 5, reason: 'Peritaje externo' })
    expect(header(init, 'Idempotency-Key')).toMatch(UUID_V7)

    server.detail = withPending
    await act(async () => answer(json(201, transferBody())))

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    const notice = screen.getByText('Solicitud enviada: queda pendiente de que Nuria Paredes la acepte.')
    await waitFor(() => expect(notice.closest('[tabindex="-1"]')).toHaveFocus())
    expect(screen.queryByRole('button', { name: 'Solicitar transferencia' })).not.toBeInTheDocument()
    expect(getIntent(intentScope('request', DEMO.Investigador.id, code))).toBeUndefined()
  })

  it('says when the server answers with a request someone already decided', async () => {
    signInAs('Investigador')
    stubApi({ detail: base, chain: [] }, async () => json(201, transferBody({ status: 'Accepted', decidedBy: nuria })))
    await open()

    await requestTo('Nuria Paredes', 'Peritaje externo')

    expect(await screen.findByText('Solicitud enviada, y Nuria Paredes ya la aceptó.')).toBeInTheDocument()
  })

  it('lets only the recipient decide, accepting once with the loaded ETag and its own key', async () => {
    signInAsUser('Custodio', NURIA)
    const server: Server = { detail: withPending, chain: [] }
    let answer: (response: Response) => void = () => {}
    const fetchMock = stubApi(server, () => new Promise((resolve) => (answer = resolve)))
    await open()

    const accept = screen.getByRole('button', { name: 'Aceptar custodia' })
    await userEvent.click(accept)
    await userEvent.click(accept)
    expect(accept).toHaveAttribute('aria-disabled', 'true')
    expect(progress()).toHaveTextContent('Enviando la aceptación…')
    server.detail = { ...base, currentCustodian: nuria }
    await act(async () => answer(json(200, transferBody({ status: 'Accepted', decidedBy: nuria, etag: '"00000000000007d2"' }))))

    expect(await screen.findByText(`Aceptaste la custodia de ${code}.`)).toBeInTheDocument()
    expect(writes(fetchMock)).toHaveLength(1)
    const [[url, init]] = writes(fetchMock)
    expect(String(url)).toBe('/api/v1/custody-transfers/7/accept')
    expect(header(init, 'If-Match')).toBe(pending.etag)
    expect(header(init, 'Idempotency-Key')).toMatch(UUID_V7)
    expect(screen.getByText('No hay ninguna transferencia pendiente.')).toBeInTheDocument()
  })

  it.each<[string, () => void]>([
    ['the current holder', () => signInAs('Custodio')],
    ['a supervisor', () => signInAs('Supervisor')],
  ])('offers no decision or request to %s', async (_, signIn) => {
    signIn()
    stubApi({ detail: withPending, chain: [] }, async () => problem(500))
    await open()

    expect(screen.getByText('Pendiente')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Aceptar custodia' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Solicitar transferencia' })).not.toBeInTheDocument()
  })

  it('on a 409 shows the transfer as the server left it, says who acted and when, and moves focus to the alert', async () => {
    signInAsUser('Custodio', NURIA)
    const server: Server = { detail: withPending, chain: [] }
    stubApi(server, async () => {
      server.detail = base // the server no longer lists it as pending
      return problem(409, {
        type: 'urn:evidence-chain:problem:invalid-transition',
        currentState: { transferId: 7, evidenceId: 1, status: 'Rejected', fromCustodianId: 4, toCustodianId: 5 },
        currentETag: '"00000000000007d2"', actedBy: nuria, actedAtUtc: '2026-10-09T09:00:00Z',
      })
    })
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No se pudo aceptar: ya la rechazaste el 09 oct 2026, 09:00 UTC (quizá en otra pestaña).')
    await waitFor(() => expect(alert).toHaveFocus())
    expect(screen.getByText('Rechazada por Nuria Paredes')).toBeInTheDocument()
    expect(screen.queryByText(/Aceptada/)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Aceptar custodia' })).not.toBeInTheDocument()
  })

  it('after a stale-version 409 on a transfer still pending, offers the decision again at the new version', async () => {
    signInAsUser('Custodio', NURIA)
    const fresh = { ...pending, etag: '"00000000000007d9"' }
    const server: Server = { detail: withPending, chain: [] }
    const fetchMock = stubApi(server, async () => {
      server.detail = { ...base, pendingTransfer: fresh }
      return problem(409, { type: 'urn:evidence-chain:problem:stale-version', currentState: { transferId: 7, status: 'Pending', toCustodianId: 5 } })
    })
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('la transferencia cambió desde que cargaste la página')
    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(2))
    expect(header(writes(fetchMock)[1][1], 'If-Match')).toBe(fresh.etag)
  })

  it('needs a reason to reject, pointing at the field, and sends it', async () => {
    signInAsUser('Custodio', NURIA)
    const server: Server = { detail: withPending, chain: [] }
    const fetchMock = stubApi(server, async () => {
      server.detail = base
      return json(200, transferBody({ status: 'Rejected', decidedBy: nuria, decisionNotes: 'Sin orden' }))
    })
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Rechazar' }))
    const dialog = screen.getByRole('dialog', { name: 'Rechazar la transferencia' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Rechazar transferencia' }))
    const field = within(dialog).getByLabelText('Motivo del rechazo')
    expect(field).toHaveAttribute('aria-invalid', 'true')
    expect(field).toHaveAccessibleDescription('Explica por qué la rechazas (al menos 3 caracteres).')
    expect(field).toHaveFocus()
    expect(writes(fetchMock)).toHaveLength(0)

    await userEvent.type(field, 'Sin orden')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Rechazar transferencia' }))

    expect(await screen.findByText('Rechazaste la transferencia; la custodia sigue con Diego Salas.')).toBeInTheDocument()
    expect(JSON.parse(String(writes(fetchMock)[0][1]?.body))).toEqual({ reason: 'Sin orden' })
  })

  it('points the request dialog’s errors at the field to fix', async () => {
    signInAs('Investigador')
    stubApi({ detail: base, chain: [] }, async () => problem(500))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))
    expect(within(dialog).getByLabelText('Custodio que la recibirá')).toHaveAttribute('aria-invalid', 'true')

    await userEvent.selectOptions(within(dialog).getByLabelText('Custodio que la recibirá'), 'Nuria Paredes')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Enviar solicitud' }))
    const reason = within(dialog).getByLabelText('Motivo')
    expect(reason).toHaveAttribute('aria-invalid', 'true')
    expect(reason).toHaveAccessibleDescription('Escribe el motivo (al menos 3 caracteres).')
    expect(reason).toHaveFocus()
    expect(within(dialog).getByLabelText('Custodio que la recibirá')).not.toHaveAttribute('aria-invalid')
  })

  it('checks an unanswered request against the re-read page and retries it with the same key', async () => {
    signInAs('Investigador')
    const server: Server = { detail: base, chain: [] }
    let release: () => void = () => {}
    const retried = new Promise<void>((resolve) => (release = resolve))
    const answers = [
      () => Promise.reject(new TypeError('Failed to fetch')),
      async () => {
        await retried // held until the test has looked at focus
        server.detail = withPending
        return json(201, transferBody())
      },
    ]
    const fetchMock = stubApi(server, () => answers.shift()!())
    await open()

    await requestTo('Nuria Paredes', 'Peritaje externo')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('No hubo respuesta del servidor y la solicitud no aparece en la cadena. Puedes reintentar: si ya había llegado, no se registrará dos veces.')
    await waitFor(() => expect(alert).toHaveFocus())
    expect(detailReads(fetchMock)).toBe(2)
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }))
    expect(screen.getByRole('heading', { name: 'Transferencia de custodia' })).toHaveFocus()
    release()

    expect(await screen.findByText('Pendiente')).toBeInTheDocument()
    const [first, second] = writes(fetchMock)
    expect(header(second[1], 'Idempotency-Key')).toBe(header(first[1], 'Idempotency-Key'))
    expect(second[1]?.body).toBe(first[1]?.body)
  })

  it('finds an unanswered request in the re-read chain, says it arrived and forgets its key', async () => {
    signInAs('Investigador')
    const server: Server = { detail: base, chain: [] }
    stubApi(server, async () => {
      server.detail = withPending
      server.chain = [event('TransferRequested', lucia, { notes: 'Peritaje externo' })]
      throw new TypeError('Failed to fetch') // it landed, but the answer was lost
    })
    await open()

    await requestTo('Nuria Paredes', 'Peritaje externo')

    expect(await screen.findByText('La solicitud sí llegó: queda pendiente de que Nuria Paredes la acepte.')).toBeInTheDocument()
    expect(getIntent(intentScope('request', DEMO.Investigador.id, code))).toBeUndefined()
  })

  it.each<[string, ChainEvent[], string]>([
    ['the acceptance arrived', [event('TransferAccepted', nuria)], 'La aceptación sí llegó: ahora la custodia es tuya.'],
    ['it was rejected in another tab', [event('TransferRejected', nuria)], 'No se pudo aceptar: ya la habías rechazado (quizá en otra pestaña).'],
  ])('checks an unanswered acceptance against the chain: %s', async (_, chain, text) => {
    signInAsUser('Custodio', NURIA)
    const server: Server = { detail: withPending, chain: [] }
    stubApi(server, async () => {
      server.detail = base
      server.chain = chain
      return problem(502)
    })
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    expect(await screen.findByText(text)).toBeInTheDocument()
  })

  it('offers the same key again for an unanswered acceptance the chain does not show', async () => {
    signInAsUser('Custodio', NURIA)
    const fetchMock = stubApi({ detail: withPending, chain: [] }, async () => problem(409, { type: 'urn:evidence-chain:problem:idempotency-in-flight' }))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('El servidor aún está procesando el primer envío de la decisión.')
    await userEvent.click(within(alert).getByRole('button', { name: 'Reintentar' }))

    await waitFor(() => expect(writes(fetchMock)).toHaveLength(2))
    const [first, second] = writes(fetchMock)
    expect(header(second[1], 'Idempotency-Key')).toBe(header(first[1], 'Idempotency-Key'))
  })

  it.each<[string, () => Promise<Response>, string, boolean]>([
    ['400', async () => problem(400, { errors: { toCustodianId: ['That custodian already holds the evidence.'] } }), 'No se pudo solicitar la transferencia: el servidor no aceptó los datos.', false],
    ['403', async () => problem(403), 'No se pudo solicitar la transferencia: solo un investigador puede pedirla.', false],
    ['404', async () => problem(404), 'No se pudo solicitar la transferencia (código 404).', false],
    ['422', async () => problem(422, { type: 'urn:evidence-chain:problem:idempotency-key-reused' }), 'esa petición ya se había usado con otros datos', false],
    ['409 without a state', async () => problem(409, { type: 'urn:evidence-chain:problem:daily-index-exhausted' }), 'el estado cambió mientras tanto', false],
    ['409 with another pending transfer', async () => problem(409, { type: 'urn:evidence-chain:problem:invalid-transition', currentState: { transferId: 8, status: 'Pending', toCustodianId: 4 }, actedBy: lucia, actedAtUtc: '2026-10-09T09:00:00Z' }), 'ya hay una transferencia pendiente para Diego Salas, pedida por ti el 09 oct 2026, 09:00 UTC.', false],
    ['409 concurrent write', async () => problem(409, { type: 'urn:evidence-chain:problem:concurrent-write' }), 'no se guardó nada. Puedes reintentar.', true],
    ['500', async () => problem(500), 'El servidor falló al responder (código 500) y la solicitud no aparece en la cadena.', true],
  ])('explains a %s answer in Spanish, offering a retry only when the same key may be sent again', async (_, answer, text, retry) => {
    signInAs('Investigador')
    stubApi({ detail: base, chain: [] }, answer)
    await open()

    await requestTo('Nuria Paredes', 'Peritaje externo')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(text)
    expect(alert).not.toHaveTextContent('That custodian')
    expect(within(alert).queryByRole('button', { name: 'Reintentar' }) !== null).toBe(retry)
    expect(getIntent(intentScope('request', DEMO.Investigador.id, code)) !== undefined).toBe(retry)
  })

  it('tells a custodian who is not the recipient that only the recipient decides', async () => {
    signInAsUser('Custodio', NURIA)
    stubApi({ detail: withPending, chain: [] }, async () => problem(403))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Aceptar custodia' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo aceptar: solo el custodio que la recibe puede decidirla.')
  })

  it('on load, brings back a request left without an answer, without taking focus, and lets it be discarded', async () => {
    signInAs('Investigador')
    const scope = intentScope('request', DEMO.Investigador.id, code)
    startIntent(scope, { toCustodianId: '5', reason: 'Peritaje externo' }) // sent before a reload
    stubApi({ detail: base, chain: [] }, async () => problem(500))
    await open()

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Tu última solicitud quedó sin confirmar y no aparece en la cadena.')
    expect(alert).not.toHaveFocus()
    await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveAccessibleDescription(/Tu última solicitud quedó sin confirmar/)
    expect(within(dialog).getByLabelText('Custodio que la recibirá')).toHaveValue('5')
    await userEvent.keyboard('{Escape}')

    await userEvent.click(within(alert).getByRole('button', { name: 'Descartar' }))

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Transferencia de custodia' })).toHaveFocus()
    expect(getIntent(scope)).toBeUndefined()
  })

  it('on load, forgets a request the chain shows arrived, and never preselects someone who now holds the evidence', async () => {
    signInAs('Investigador')
    const scope = intentScope('request', DEMO.Investigador.id, code)
    startIntent(scope, { toCustodianId: '5', reason: 'Peritaje externo' })
    stubApi({ detail: { ...base, currentCustodian: nuria }, chain: [event('TransferRequested', lucia, { notes: 'Peritaje externo' })] }, async () => problem(500))
    await open()

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(getIntent(scope)).toBeUndefined()

    startIntent(scope, { toCustodianId: '5', reason: 'Otra vez' }) // unresolved, to someone who now holds it
    await userEvent.click(screen.getByRole('button', { name: 'Solicitar transferencia' }))
    expect(within(screen.getByRole('dialog')).getByLabelText('Custodio que la recibirá')).toHaveValue('')
  })

  it('returns focus to the opener when the dialog closes with Cancelar or the close button', async () => {
    signInAs('Investigador')
    stubApi({ detail: base, chain: [] }, async () => problem(500))
    await open()
    const opener = screen.getByRole('button', { name: 'Solicitar transferencia' })

    for (const close of ['Cancelar', 'Cerrar']) {
      await userEvent.click(opener)
      await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: close }))
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(opener).toHaveFocus()
    }
  })
})
