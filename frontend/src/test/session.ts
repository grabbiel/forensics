import { saveSession, type Role, type SessionUser } from '../auth/session'

/** The seeded demo users, one per role. */
export const DEMO: Record<Role, SessionUser> = {
  Investigador: { id: 1, userName: 'investigador.demo', displayName: 'Lucía Ferrer', role: 'Investigador' },
  Custodio: { id: 4, userName: 'custodio.demo', displayName: 'Diego Salas', role: 'Custodio' },
  Supervisor: { id: 10, userName: 'supervisor.demo', displayName: 'Elena Ruiz', role: 'Supervisor' },
}

/** Starts a test signed in, with a token valid for an hour. */
export function signInAs(role: Role = 'Investigador'): void {
  saveSession({ accessToken: `token-${role}`, expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(), user: DEMO[role] })
}
