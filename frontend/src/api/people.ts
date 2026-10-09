import type { Role } from '../auth/session'
import { getJson } from './client'

export interface Person {
  id: number
  displayName: string
  role: Role
}

// The people list changes only with the seed, so one read per role serves the whole visit; a failed read is retried.
const cache = new Map<string, Promise<Person[]>>()

/** Users by display name, optionally one role (the custodians a filter or a transfer can name). */
export function listPeople(role?: Role, signal?: AbortSignal): Promise<Person[]> {
  const key = role ?? 'all'
  let people = cache.get(key)
  if (!people) {
    // Not tied to one navigation's signal: other pages share this read.
    people = getJson<Person[]>(role ? `/api/v1/people?role=${role}` : '/api/v1/people')
    people.catch(() => cache.delete(key))
    cache.set(key, people)
  }
  return signal ? Promise.race([people, aborted(signal)]) : people
}

/** Forgets cached lists (tests, sign-out). */
export function forgetPeople(): void {
  cache.clear()
}

function aborted(signal: AbortSignal): Promise<never> {
  return new Promise((_, reject) => {
    if (signal.aborted) reject(signal.reason)
    signal.addEventListener('abort', () => reject(signal.reason), { once: true })
  })
}
