import type { Role } from '../auth/session'
import { getJson } from './client'

export interface Person {
  id: number
  userName: string
  displayName: string
  role: Role
}

/** Users by display name, optionally one role (the custodians a filter or a transfer can name). */
export function listPeople(role?: Role, signal?: AbortSignal): Promise<Person[]> {
  return getJson<Person[]>(role ? `/api/v1/people?role=${role}` : '/api/v1/people', signal)
}
