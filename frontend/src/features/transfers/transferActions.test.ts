import type { ActionFunctionArgs, LoaderFunctionArgs } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { DEMO, signInAs } from '../../test/session'
import { verifyLoader } from '../evidence/evidenceLoader'
import { getIntent, intentScope, startIntent } from './pendingIntent'
import { decisionAction, requestTransferAction, type WriteResult } from './transferActions'

const code = 'LOG202609110007'
const transfer = { transferId: 7, evidenceCode: code, status: 'Pending', etag: '"00000000000007d1"' }
const requestScope = intentScope('request', DEMO.Investigador.id, code)

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

const requestArgs = (fields: Record<string, string>) => argsOf(post(`/evidence/${code}/transfer`, fields), { id: code })

/** A request intent as the panel starts it, and the action call that sends it. */
function sendRequest() {
  const intent = startIntent(requestScope, { toCustodianId: '5', reason: 'Peritaje' })
  return { intent, result: run(requestTransferAction(requestArgs({ ...intent.fields, idempotencyKey: intent.idempotencyKey }))) }
}

describe('transfer actions', () => {
  it('requests with the pending intent’s key, reports a replay, and clears that intent', async () => {
    signInAs('Investigador')
    const fetchMock = vi.fn<typeof fetch>(async () => reply(201, transfer, 'application/json', { 'Idempotent-Replayed': 'true' }))
    vi.stubGlobal('fetch', fetchMock)

    const { intent, result } = sendRequest()
    const { data, status } = await result

    expect(status).toBe(201)
    expect(data).toEqual({ outcome: 'done', write: 'request', transfer, replayed: true })
    const [, init] = fetchMock.mock.calls[0]
    expect(JSON.parse(String(init?.body))).toEqual({ evidenceCode: code, toCustodianId: 5, reason: 'Peritaje' })
    expect(init?.headers).toMatchObject({ 'Idempotency-Key': intent.idempotencyKey })
    expect(getIntent(requestScope)).toBeUndefined()
  })

  it('leaves a newer intent alone when an older write’s answer arrives', async () => {
    signInAs('Investigador')
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(201, transfer)))
    const { result } = sendRequest()
    const newer = startIntent(requestScope, { toCustodianId: '6', reason: 'Otra cosa' })

    await result

    expect(getIntent(requestScope)?.idempotencyKey).toBe(newer.idempotencyKey)
  })

  it('answers a refusal with the problem and its status, and clears the intent', async () => {
    signInAs('Investigador')
    const problem = { status: 409, type: 'urn:evidence-chain:problem:invalid-transition', currentState: { status: 'Pending' } }
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(409, problem, 'application/problem+json')))

    const { data, status } = await sendRequest().result

    expect(status).toBe(409)
    expect(data).toEqual({ outcome: 'refused', write: 'request', status: 409, problem })
    expect(getIntent(requestScope)).toBeUndefined()
  })

  it('keeps the intent after a concurrent-write refusal, as nothing was saved and the same key may be sent again', async () => {
    signInAs('Investigador')
    const problem = { status: 409, type: 'urn:evidence-chain:problem:concurrent-write' }
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(409, problem, 'application/problem+json')))

    const { intent, result } = sendRequest()
    expect((await result).data).toEqual({ outcome: 'refused', write: 'request', status: 409, problem })
    expect(getIntent(requestScope)?.idempotencyKey).toBe(intent.idempotencyKey)
  })

  it('calls a 429 throttled with its wait, keeping the intent, as the server turned the write away before running it', async () => {
    signInAs('Investigador')
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(429, { status: 429 }, 'application/problem+json', { 'Retry-After': '3' })))

    const { intent, result } = sendRequest()
    const { data, status } = await result

    expect([data, status]).toEqual([{ outcome: 'throttled', write: 'request', retryAfterSeconds: 3 }, 429])
    expect(getIntent(requestScope)?.idempotencyKey).toBe(intent.idempotencyKey)
  })

  it('calls a write unknown, keeping its intent, after no answer, a timeout, a server error or an answer that the first send still runs', async () => {
    signInAs('Investigador')
    const inFlight = { status: 409, type: 'urn:evidence-chain:problem:idempotency-in-flight' }
    const endings: [() => Promise<Response>, { status: number; problem?: unknown }][] = [
      [() => Promise.reject(new TypeError('Failed to fetch')), { status: 0 }],
      [() => Promise.reject(new DOMException('timed out', 'TimeoutError')), { status: 0 }],
      [async () => reply(500, { status: 500 }, 'application/problem+json'), { status: 500, problem: { status: 500 } }],
      [async () => new Response('<html>gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } }), { status: 502, problem: undefined }],
      [async () => reply(409, inFlight, 'application/problem+json'), { status: 409, problem: inFlight }],
    ]
    for (const [answer, seen] of endings) {
      vi.stubGlobal('fetch', vi.fn<typeof fetch>(answer))
      const { intent, result } = sendRequest()
      const { data, status } = await result
      expect([data, status]).toEqual([{ outcome: 'unknown', write: 'request', ...seen }, 503])
      expect(getIntent(requestScope)?.idempotencyKey).toBe(intent.idempotencyKey)
    }
  })

  it('sends the user to sign in again on a 401, keeping the intent for after', async () => {
    signInAs('Investigador')
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(401, { status: 401 }, 'application/problem+json')))

    const intent = startIntent(requestScope, { toCustodianId: '5', reason: 'Peritaje' })
    const thrown = await requestTransferAction(requestArgs({ ...intent.fields, idempotencyKey: intent.idempotencyKey })).catch((error: unknown) => error)

    expect(thrown).toBeInstanceOf(Response)
    expect((thrown as Response).headers.get('Location')).toBe(`/login?redirectTo=${encodeURIComponent(`/evidence/${code}`)}`)
    expect(getIntent(requestScope)?.idempotencyKey).toBe(intent.idempotencyKey)
  })

  it('decides with If-Match and its own key, sending notes on accept and the reason on reject', async () => {
    signInAs('Custodio')
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, { ...transfer, status: 'Accepted' }))
    vi.stubGlobal('fetch', fetchMock)

    const accepted = await run(decisionAction('accept')(argsOf(post('/transfers/7/accept', { evidenceCode: code, etag: transfer.etag, idempotencyKey: 'a', notes: 'Recibida' }), { transferId: '7' })))
    const rejected = await run(decisionAction('reject')(argsOf(post('/transfers/7/reject', { evidenceCode: code, etag: transfer.etag, idempotencyKey: 'r', reason: 'Sin orden' }), { transferId: '7' })))

    expect([accepted.data.write, rejected.data.write]).toEqual(['accept', 'reject'])
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

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(429, { status: 429 }, 'application/problem+json', { 'Retry-After': '9' })))
    expect(await verifyLoader(args)).toEqual({ ok: false, status: 429, problem: { status: 429 }, retryAfterSeconds: 9 })

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch'))))
    expect(await verifyLoader(args)).toEqual({ ok: false, status: 0 })
  })
})
