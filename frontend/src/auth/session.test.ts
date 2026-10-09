import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Session } from './session'

const session = (expiresInMs: number): Session => ({
  accessToken: 'abc',
  expiresAtUtc: new Date(Date.now() + expiresInMs).toISOString(),
  user: { id: 4, userName: 'custodio.demo', displayName: 'Diego Salas', role: 'Custodio' },
})

/** A fresh copy of the module, as after a page reload: nothing in memory, only what sessionStorage kept. */
async function reload() {
  vi.resetModules()
  return import('./session')
}

describe('session', () => {
  afterEach(() => sessionStorage.clear())

  it('survives a reload of the tab through sessionStorage', async () => {
    const before = await reload()
    before.saveSession(session(3_600_000))

    const after = await reload()
    expect(after.getSession()?.user.displayName).toBe('Diego Salas')
  })

  it('treats a token about to expire as signed out and forgets it', async () => {
    const { saveSession, getSession } = await reload()
    saveSession(session(30_000)) // inside the one-minute margin

    expect(getSession()).toBeNull()
    expect(sessionStorage.length).toBe(0)
  })

  it('keeps working in memory when storage is blocked', async () => {
    const { saveSession, getSession, clearSession } = await reload()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError')
    })

    saveSession(session(3_600_000))
    expect(getSession()?.accessToken).toBe('abc')
    clearSession()
    expect(getSession()).toBeNull()
  })
})
