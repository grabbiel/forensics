import type { ActionFunctionArgs, LoaderFunctionArgs } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { signInAs } from '../../test/session'
import { verifyLoader } from '../evidence/evidenceLoader'
import { decisionAction, requestTransferAction, type WriteResult } from './transferActions'

const transfer = { transferId: 7, evidenceCode: 'LOG202609110007', status: 'Pending', etag: '"00000000000007d1"' }

function reply(status: number, body: unknown, contentType = 'application/json', headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType, ...headers } })
}

function post(url: string, fields: Record<string, string>) {
  return new Request(`http://localhost${url}`, { method: 'POST', body: new URLSearchParams(fields) })
}

/** What the router passes a loader or action. */
function argsOf(request: Request, params: Record<string, string>) {
  return { request, params, url: new URL(request.url), pattern: '', context: {} } as unknown as ActionFunctionArgs & LoaderFunctionArgs
}

/** The action's answer: what the page reads, and the status the router sees. */
async function run(result: Promise<unknown>) {
  const { data, init } = (await result) as { data: WriteResult; init: { status: number } }
  return { data, status: init.status }
}

const requestArgs = (fields: Record<string, string>) => argsOf(post('/evidence/LOG202609110007/transfer', fields), { id: 'LOG202609110007' })

describe('transfer actions', () => {
  it('requests with the pending intent’s key and reports a replay', async () => {
    signInAs('Investigador')
    const fetchMock = vi.fn<typeof fetch>(async () => reply(201, transfer, 'application/json', { 'Idempotent-Replayed': 'true' }))
    vi.stubGlobal('fetch', fetchMock)

    const { data, status } = await run(requestTransferAction(requestArgs({ toCustodianId: '5', reason: 'Peritaje', idempotencyKey: 'key-1' })))

    expect(status).toBe(201)
    expect(data).toEqual({ outcome: 'done', transfer, replayed: true })
    const [, init] = fetchMock.mock.calls[0]
    expect(JSON.parse(String(init?.body))).toEqual({ evidenceCode: 'LOG202609110007', toCustodianId: 5, reason: 'Peritaje' })
    expect(init?.headers).toMatchObject({ 'Idempotency-Key': 'key-1' })
  })

  it('answers a refusal with the problem and its status, so the router does not revalidate', async () => {
    signInAs('Investigador')
    const problem = { status: 409, type: 'urn:evidence-chain:problem:invalid-transition', currentState: { status: 'Pending' } }
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(409, problem, 'application/problem+json')))

    const { data, status } = await run(requestTransferAction(requestArgs({ toCustodianId: '5', reason: 'x', idempotencyKey: 'k' })))

    expect(status).toBe(409)
    expect(data).toEqual({ outcome: 'refused', status: 409, problem })
  })

  it('calls a write with no answer unknown, whether the network failed or it timed out', async () => {
    signInAs('Investigador')
    for (const failure of [new TypeError('Failed to fetch'), new DOMException('timed out', 'TimeoutError')]) {
      vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Promise.reject(failure)))
      const { data, status } = await run(requestTransferAction(requestArgs({ toCustodianId: '5', reason: 'x', idempotencyKey: 'k' })))
      expect([data, status]).toEqual([{ outcome: 'unknown' }, 503])
    }
  })

  it('decides with If-Match and its own key, sending notes on accept and the reason on reject', async () => {
    signInAs('Custodio')
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, { ...transfer, status: 'Accepted' }))
    vi.stubGlobal('fetch', fetchMock)

    await decisionAction('accept')(argsOf(post('/transfers/7/accept', { etag: transfer.etag, idempotencyKey: 'a', notes: 'Recibida' }), { transferId: '7' }))
    await decisionAction('reject')(argsOf(post('/transfers/7/reject', { etag: transfer.etag, idempotencyKey: 'r', reason: 'Sin orden' }), { transferId: '7' }))

    const [accept, reject] = fetchMock.mock.calls
    expect(String(accept[0])).toBe('/api/v1/custody-transfers/7/accept')
    expect(accept[1]?.headers).toMatchObject({ 'If-Match': transfer.etag, 'Idempotency-Key': 'a' })
    expect(JSON.parse(String(accept[1]?.body))).toEqual({ notes: 'Recibida' })
    expect(String(reject[0])).toBe('/api/v1/custody-transfers/7/reject')
    expect(JSON.parse(String(reject[1]?.body))).toEqual({ reason: 'Sin orden' })
  })
})

describe('verify resource route', () => {
  const args = argsOf(new Request('http://localhost/evidence/LOG1/verify'), { id: 'LOG1' })

  it('returns the report, a refusal or no answer, never throwing', async () => {
    signInAs('Supervisor')
    const report = { code: 'LOG1', valid: false, verifiedThroughSeq: 2, eventCount: 5, checkedAtUtc: '2026-10-09T00:00:00Z', firstInvalid: { eventId: 3, seq: 3, reason: 'MAC_MISMATCH', detail: 'x' } }

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(200, report)))
    expect(await verifyLoader(args)).toEqual({ ok: true, report })

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(404, { status: 404 }, 'application/problem+json')))
    expect(await verifyLoader(args)).toEqual({ ok: false, status: 404, problem: { status: 404 } })

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch'))))
    expect(await verifyLoader(args)).toEqual({ ok: false, status: 0 })
  })
})
