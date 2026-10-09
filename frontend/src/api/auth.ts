import type { Session } from '../auth/session'
import { postJson } from './client'

/** Signs in as a seeded demo user (no password, by design of the demo). */
export async function signIn(userName: string, signal?: AbortSignal): Promise<Session> {
  const { body } = await postJson<Session & { tokenType: string }>('/api/v1/auth/token', { userName }, { signal })
  return { accessToken: body.accessToken, expiresAtUtc: body.expiresAtUtc, user: body.user }
}
