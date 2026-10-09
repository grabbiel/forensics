import { describe, expect, it, vi } from 'vitest'
import { uuidv7 } from '../../lib/uuid'
import { clearAllIntents, clearIntent, getIntent, intentScope, startIntent } from './pendingIntent'

const blocked = () => {
  throw new DOMException('blocked', 'SecurityError')
}

describe('pending intent', () => {
  it('reuses the key for the same write, survives in sessionStorage, and is gone once cleared', () => {
    const first = startIntent('request:LOG1', { toCustodianId: '5', reason: 'Peritaje' })
    const again = startIntent('request:LOG1', { reason: 'Peritaje', toCustodianId: '5' }) // field order does not matter

    expect(again.idempotencyKey).toBe(first.idempotencyKey)
    expect(JSON.parse(sessionStorage.getItem('evidence-chain:intent:request:LOG1')!).idempotencyKey).toBe(first.idempotencyKey)
    clearIntent('request:LOG1')
    expect(getIntent('request:LOG1')).toBeUndefined()
  })

  it('starts a new key when the write changes, since one key cannot carry two bodies', () => {
    const first = startIntent('request:LOG1', { toCustodianId: '5', reason: 'Peritaje' })
    const changed = startIntent('request:LOG1', { toCustodianId: '6', reason: 'Peritaje' })

    expect(changed.idempotencyKey).not.toBe(first.idempotencyKey)
    expect(getIntent('request:LOG1')?.fields.toCustodianId).toBe('6')
  })

  it('keeps intents in memory when storage is blocked, for writes and for reads', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked)
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked)
    const intent = startIntent('request:LOG1', { toCustodianId: '5', reason: 'x' })
    expect(getIntent('request:LOG1')?.idempotencyKey).toBe(intent.idempotencyKey)
  })

  it('never lets an older stored intent win over a newer one it could not store', () => {
    startIntent('request:LOG1', { toCustodianId: '5', reason: 'x' })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked)
    const newer = startIntent('request:LOG1', { toCustodianId: '6', reason: 'y' })
    expect(getIntent('request:LOG1')?.idempotencyKey).toBe(newer.idempotencyKey)
  })

  it('clears only the intent sent with a given key, so a late answer leaves a newer write alone', () => {
    const first = startIntent('request:LOG1', { toCustodianId: '5', reason: 'x' })
    const newer = startIntent('request:LOG1', { toCustodianId: '6', reason: 'y' })
    clearIntent('request:LOG1', first.idempotencyKey)
    expect(getIntent('request:LOG1')?.idempotencyKey).toBe(newer.idempotencyKey)
  })

  it('keeps each user’s intents apart and forgets them all on sign-out', () => {
    startIntent(intentScope('request', 1, 'LOG1'), { toCustodianId: '5', reason: 'x' })
    sessionStorage.setItem('unrelated', 'kept')
    expect(getIntent(intentScope('request', 2, 'LOG1'))).toBeUndefined()

    clearAllIntents()

    expect(getIntent(intentScope('request', 1, 'LOG1'))).toBeUndefined()
    expect(sessionStorage.getItem('unrelated')).toBe('kept')
  })
})

describe('uuidv7', () => {
  it('is a version 7, RFC-variant UUID that starts with the time', () => {
    const key = uuidv7(Date.UTC(2026, 9, 9))
    expect(key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(key.replace(/-/g, '').slice(0, 12)).toBe(Date.UTC(2026, 9, 9).toString(16).padStart(12, '0'))
  })
})
