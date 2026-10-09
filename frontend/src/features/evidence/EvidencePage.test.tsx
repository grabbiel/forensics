import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { EvidenceChain, EvidenceDetail, VerificationReport } from '../../api/evidence'
import { routes } from '../../routes'
import { signInAs } from '../../test/session'

const person = (id: number, displayName: string) => ({ id, displayName })
const code = 'LOG202609110007'

const detail: EvidenceDetail = {
  code,
  typeCode: 'LOG',
  description: 'Log del firewall fw-edge-01',
  capturedAtUtc: '2026-09-11T07:00:00Z',
  registeredAtUtc: '2026-09-11T08:00:00Z',
  registeredBy: person(1, 'Lucía Ferrer'),
  initialCustodian: person(4, 'Diego Salas'),
  currentCustodian: person(4, 'Diego Salas'),
  eventCount: 2,
  lastEventAtUtc: '2026-09-28T00:00:00Z',
  content: { sha256: 'ab'.repeat(32), byteLength: 2048, mediaType: 'text/plain; charset=utf-8' },
  integrity: { status: 'Unverified', checkedAtUtc: null, checkedThroughSeq: null },
  pendingTransfer: {
    transferId: 7, status: 'Pending', from: person(4, 'Diego Salas'), to: person(5, 'Nuria Paredes'), requestedBy: person(1, 'Lucía Ferrer'),
    requestedAtUtc: '2026-09-28T00:00:00Z', reason: 'Análisis en laboratorio', etag: '"00000000000007d1"',
  },
  anomalies: [{
    transferId: 7, kind: 'Overdue', severity: 'Medium', requestedAtUtc: '2026-09-28T00:00:00Z', elapsedSeconds: 259_200, deadlineSeconds: 172_800,
    explanation: 'Pendiente desde hace 72 h; el plazo de aceptación es 48 h (24 h de retraso).',
  }],
}

const chain: EvidenceChain = {
  code,
  events: [
    { eventId: 11, seq: 1, kind: 'EvidenceRegistered', occurredAtUtc: '2026-09-11T08:00:00Z', actor: person(1, 'Lucía Ferrer'), from: null, to: person(4, 'Diego Salas'), transferId: null, notes: 'Registro inicial', keyId: 'dev', mac: 'aa'.repeat(32), prevMac: null },
    { eventId: 12, seq: 2, kind: 'TransferRequested', occurredAtUtc: '2026-09-28T00:00:00Z', actor: person(1, 'Lucía Ferrer'), from: person(4, 'Diego Salas'), to: person(5, 'Nuria Paredes'), transferId: 7, notes: 'Análisis en laboratorio', keyId: 'dev', mac: 'bb'.repeat(32), prevMac: 'aa'.repeat(32) },
  ],
}

function json(status: number, body: unknown, contentType = 'application/json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

/** The evidence and its chain; verification answers whatever the test sets. */
function stubApi(verify: () => Promise<Response>, events = chain.events) {
  const fetchMock = vi.fn<typeof fetch>(async (input) => {
    const url = String(input)
    if (url === `/api/v1/evidence/${code}`) return json(200, detail)
    if (url === `/api/v1/evidence/${code}/chain`) return json(200, { code, events })
    if (url === `/api/v1/evidence/${code}/chain/verify`) return verify()
    if (url.startsWith('/api/v1/people')) return json(200, [])
    return json(404, { status: 404 }, 'application/problem+json')
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const report = (overrides: Partial<VerificationReport>): VerificationReport => ({
  code, valid: true, verifiedThroughSeq: 2, eventCount: 2, checkedAtUtc: '2026-10-09T00:00:00Z', firstInvalid: null, ...overrides,
})

/** The verification's live region; the transfer panel has its own. */
const verifyStatus = () => within(screen.getByRole('button', { name: 'Verificar cadena' }).parentElement!).getByRole('status')

async function open() {
  render(<RouterProvider router={createMemoryRouter(routes, { initialEntries: [`/evidence/${code}`] })} />)
  return screen.findByRole('heading', { level: 1, name: code })
}

describe('EvidencePage', () => {
  beforeEach(() => signInAs('Supervisor'))

  it('highlights an anomaly with an icon, its severity in words and the rule’s explanation', async () => {
    stubApi(async () => json(200, report({})))
    await open()

    const anomalies = screen.getByRole('region', { name: 'Anomalías' })
    expect(within(anomalies).getByText('Transferencia vencida')).toBeInTheDocument()
    expect(within(anomalies).getByText('Severidad media')).toBeInTheDocument()
    expect(within(anomalies).getByText(detail.anomalies[0].explanation)).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Transferencia de custodia' })).toHaveTextContent('De Diego Salas a Nuria Paredes, pedida por Lucía Ferrer')
    expect(screen.getByText(detail.content.sha256)).toBeInTheDocument()
  })

  it('lists the chain in order, each event in words with its MAC and key', async () => {
    stubApi(async () => json(200, report({})))
    await open()

    const items = within(screen.getByRole('region', { name: 'Cadena de custodia' })).getAllByRole('listitem')
    expect(items.map((item) => item.querySelector('.timeline__seq')?.textContent)).toEqual(['#1', '#2'])
    expect(items[1]).toHaveTextContent('Lucía Ferrer pidió pasarla de Diego Salas a Nuria Paredes')
    expect(items[1]).toHaveTextContent('Análisis en laboratorio')
    expect(items[1]).toHaveTextContent('MAC bbbbbbbbbbbbbbbb… · clave dev')
  })

  it('verifies on demand and says the chain is intact', async () => {
    stubApi(async () => json(200, report({})))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))

    await waitFor(() => expect(verifyStatus()).toHaveTextContent('Cadena íntegra: los 2 eventos y el contenido coinciden con lo firmado.'))
    expect(screen.getAllByText('Íntegra').length).toBeGreaterThan(0)
  })

  it('runs one verification at a time, however often the button is pressed', async () => {
    let release: (response: Response) => void = () => {}
    const fetchMock = stubApi(() => new Promise<Response>((resolve) => (release = resolve)))
    await open()
    const button = screen.getByRole('button', { name: 'Verificar cadena' })

    await userEvent.click(button)
    expect(button).toHaveAttribute('aria-disabled', 'true')
    expect(verifyStatus()).toHaveTextContent('Verificando la cadena…')
    await userEvent.click(button)
    release(json(200, report({ eventCount: 1, verifiedThroughSeq: 1 })))

    await waitFor(() => expect(verifyStatus()).toHaveTextContent('Cadena íntegra: el evento y el contenido coinciden con lo firmado.'))
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/chain/verify'))).toHaveLength(1)
  })

  it('shows the first invalid event inline and marks it in the timeline', async () => {
    stubApi(async () => json(200, report({ valid: false, verifiedThroughSeq: 1, firstInvalid: { eventId: 12, seq: 2, reason: 'MAC_MISMATCH', detail: 'El MAC no corresponde a los datos del evento.' } })))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))

    const status = await screen.findByText(/Cadena alterada en el/)
    expect(status).toHaveTextContent('Cadena alterada en el evento #2: El MAC no corresponde a los datos del evento. MAC_MISMATCH')
    expect(within(status).getByRole('link', { name: 'evento #2' })).toHaveAttribute('href', '#event-2')
    const marked = document.getElementById('event-2')!
    expect(marked).toHaveTextContent('Primer evento inválido')
    expect(document.getElementById('event-1')).toHaveTextContent('(verificado)')
    expect(screen.getAllByText('Alterada').length).toBeGreaterThan(0)
  })

  it('names a missing event in its own row and moves focus there from the link', async () => {
    const withGap = [chain.events[0], { ...chain.events[1], eventId: 13, seq: 3 }]
    stubApi(async () => json(200, report({ valid: false, verifiedThroughSeq: 1, firstInvalid: { eventId: 13, seq: 2, reason: 'SEQUENCE_GAP', detail: 'Se esperaba el evento 2 y aparece el 3.' } })), withGap)
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))
    const link = await screen.findByRole('link', { name: 'evento #2' })

    const items = within(screen.getByRole('region', { name: 'Cadena de custodia' })).getAllByRole('listitem')
    expect(items.map((item) => item.querySelector('.timeline__seq')?.textContent)).toEqual(['#1', '#2', '#3'])
    expect(items[1]).toHaveTextContent('Falta el evento #2 · Primer evento inválido')
    expect(items[2]).not.toHaveTextContent('Primer evento inválido')
    await userEvent.click(link)
    expect(items[1]).toHaveFocus()
  })

  it('names an invalid event the timeline lacks without linking to it', async () => {
    stubApi(async () => json(200, report({ valid: false, verifiedThroughSeq: 2, firstInvalid: { eventId: 99, seq: 3, reason: 'HEAD_MISMATCH', detail: 'La cabecera de la evidencia no coincide con su último evento.' } })))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))

    await waitFor(() => expect(verifyStatus()).toHaveTextContent('Cadena alterada en el evento #3'))
    expect(screen.queryByRole('link', { name: 'evento #3' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Primer evento inválido/)).not.toBeInTheDocument()
  })

  it('keeps the last report when a later verification gets no answer', async () => {
    const fetchMock = stubApi(async () => json(200, report({ valid: false, verifiedThroughSeq: 1, firstInvalid: { eventId: 12, seq: 2, reason: 'MAC_MISMATCH', detail: 'El MAC no corresponde a los datos del evento.' } })))
    await open()
    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))
    await screen.findByRole('link', { name: 'evento #2' })

    fetchMock.mockImplementation(async (input) => {
      if (String(input).endsWith('/chain/verify')) throw new TypeError('Failed to fetch')
      return json(404, { status: 404 }, 'application/problem+json')
    })
    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))

    await waitFor(() => expect(verifyStatus()).toHaveTextContent('Sin conexión: no se pudo verificar.'))
    expect(document.getElementById('event-2')).toHaveTextContent('Primer evento inválido')
    expect(screen.getAllByText('Alterada').length).toBeGreaterThan(0)
  })

  it('explains a verification that got no answer, and the page stays', async () => {
    stubApi(async () => Promise.reject(new TypeError('Failed to fetch')))
    await open()

    await userEvent.click(screen.getByRole('button', { name: 'Verificar cadena' }))

    expect(await screen.findByText('Sin conexión: no se pudo verificar. Inténtalo de nuevo.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: code })).toBeInTheDocument()
  })
})
