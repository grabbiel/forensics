import { afterEach, describe, expect, it, vi } from 'vitest'
import { uuidv7 } from '../../lib/uuid'
import { clearIntent, getIntent, startIntent } from './pendingIntent'

describe('pending intent', () => {
  afterEach(() => clearIntent('request:LOG1'))

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

  it('keeps intents in memory when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })
    const intent = startIntent('request:LOG1', { toCustodianId: '5', reason: 'x' })
    expect(getIntent('request:LOG1')?.idempotencyKey).toBe(intent.idempotencyKey)
  })
})

describe('uuidv7', () => {
  it('is a version 7, RFC-variant UUID that starts with the time', () => {
    const key = uuidv7(Date.UTC(2026, 9, 9))
    expect(key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(key.replace(/-/g, '').slice(0, 12)).toBe(Date.UTC(2026, 9, 9).toString(16).padStart(12, '0'))
  })
})
