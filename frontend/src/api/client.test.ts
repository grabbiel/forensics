import { describe, expect, it, vi } from 'vitest'
import { signInAs } from '../test/session'
import { ApiError, getJson, postJson } from './client'

function reply(status: number, body: unknown, contentType = 'application/json', headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType, ...headers } })
}

describe('api client', () => {
  it('sends the bearer token only when signed in', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => reply(200, {}))
    vi.stubGlobal('fetch', fetchMock)

    await getJson('/api/v1/evidence')
    signInAs('Supervisor')
    await getJson('/api/v1/evidence')

    expect(fetchMock.mock.calls[0][1]?.headers).not.toHaveProperty('Authorization')
    expect(fetchMock.mock.calls[1][1]?.headers).toMatchObject({ Authorization: 'Bearer token-Supervisor' })
  })

  it('reads problem details only from problem+json bodies', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(404, { title: 'Not Found', detail: 'nope' }, 'application/problem+json')))
    const problem = await getJson('/x').catch((error: ApiError) => error)
    expect(problem).toBeInstanceOf(ApiError)
    expect((problem as ApiError).problem?.detail).toBe('nope')

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response('<html>gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } })))
    const gateway = await getJson('/x').catch((error: ApiError) => error)
    expect(((gateway as ApiError).status, (gateway as ApiError).problem)).toBeUndefined()
  })

  it('sends writes once with their headers and never retries a 409', async () => {
    signInAs('Custodio')
    const fetchMock = vi.fn<typeof fetch>(async () => reply(409, { status: 409, currentState: { status: 'Accepted' } }, 'application/problem+json'))
    vi.stubGlobal('fetch', fetchMock)

    const conflict = await postJson('/api/v1/custody-transfers/7/accept', { notes: 'ok' }, { headers: { 'If-Match': '"01"', 'Idempotency-Key': 'k' } }).catch((error: ApiError) => error)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect((conflict as ApiError).status).toBe(409)
    const [, init] = fetchMock.mock.calls[0]
    expect(init).toMatchObject({ method: 'POST', body: '{"notes":"ok"}' })
    expect(init?.headers).toMatchObject({ 'Content-Type': 'application/json', 'If-Match': '"01"', 'Idempotency-Key': 'k', Authorization: 'Bearer token-Custodio' })
  })

  it('returns a write’s status, body and headers', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => reply(201, { transferId: 9 }, 'application/json', { ETag: '"0a"', 'Idempotent-Replayed': 'true' })))

    const sent = await postJson<{ transferId: number }>('/api/v1/custody-transfers', {})

    expect([sent.status, sent.body.transferId, sent.headers.get('ETag'), sent.headers.get('Idempotent-Replayed')]).toEqual([201, 9, '"0a"', 'true'])
  })
})
