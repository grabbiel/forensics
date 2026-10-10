import { ArrowRight, LinkSimple, SealCheck, ShieldCheck, UserCircle } from '@phosphor-icons/react'
import type { Icon } from '@phosphor-icons/react'
import type { FormEvent } from 'react'
import { Form, useActionData, useNavigation, useSearchParams } from 'react-router'
import type { Role } from '../../auth/session'
import { READY_SENTENCE, useWaited } from '../../lib/useWaited'
import type { loginAction } from './loginRoute'

/** The demo personas; every seeded user can sign in, these three cover the roles. */
const DEMO_USERS: { userName: string; displayName: string; role: Role; can: string; icon: Icon }[] = [
  { userName: 'investigador.demo', displayName: 'Lucía Ferrer', role: 'Investigador', can: 'Solicita transferencias de custodia.', icon: UserCircle },
  { userName: 'custodio.demo', displayName: 'Diego Salas', role: 'Custodio', can: 'Acepta o rechaza las que le envían.', icon: ShieldCheck },
  { userName: 'supervisor.demo', displayName: 'Elena Ruiz', role: 'Supervisor', can: 'Revisa evidencias y verifica cadenas.', icon: SealCheck },
]

/** Demo sign-in: pick a person, no password. The token lives in this tab only. */
export function LoginPage() {
  const result = useActionData<typeof loginAction>()
  const navigation = useNavigation()
  // Busy from the submission until the page it leads to has loaded, not only while the token is requested.
  const busy = navigation.state !== 'idle' && (navigation.formAction?.startsWith('/login') ?? false)
  const pendingUser = busy ? String(navigation.formData?.get('userName') ?? '') : null
  // After a 429, another attempt before the server's wait is over would only be refused again.
  const waited = useWaited(result?.retryAfterSeconds, result)
  const held = busy || !waited
  // aria-disabled keeps focus on the pressed button; this keeps it from submitting again meanwhile.
  const holdWhileHeld = (event: FormEvent<HTMLFormElement>) => held && event.preventDefault()
  const [searchParams] = useSearchParams()
  const action = searchParams.size > 0 ? `/login?${searchParams}` : '/login'

  return (
    <div className="login">
      <header className="login__brand">
        <span className="brand__mark">
          <LinkSimple size={18} weight="bold" aria-hidden="true" />
        </span>
        <span className="brand__name">Evidence Chain</span>
      </header>

      <main id="main" className="panel login__panel" aria-labelledby="login-title">
        <h1 id="login-title" className="panel__title">
          Iniciar sesión
        </h1>
        <p className="panel__hint">Entorno de demostración: elige con quién entrar. No hace falta contraseña.</p>

        {result?.error && (
          <p className="notice notice--error" role="alert">
            {result.error}
          </p>
        )}
        {/* Outside the alert, so the end of a wait is said politely instead of the alert again. */}
        <p className="visually-hidden" role="status">
          {result?.retryAfterSeconds !== undefined && waited ? READY_SENTENCE : ''}
        </p>

        <ul className="personas">
          {DEMO_USERS.map(({ userName, displayName, role, can, icon: PersonaIcon }) => (
            <li key={userName}>
              <Form method="post" action={action} onSubmit={holdWhileHeld}>
                <input type="hidden" name="userName" value={userName} />
                <button type="submit" className={waited ? 'persona' : 'persona persona--waiting'} aria-disabled={held} disabled={busy && pendingUser !== userName}>
                  <PersonaIcon size={28} aria-hidden="true" />
                  <span className="persona__text">
                    <span className="persona__name">{displayName}</span>
                    <span className="persona__role">{role}</span>
                    <span className="persona__can">{can}</span>
                  </span>
                  <ArrowRight size={18} aria-hidden="true" className="persona__go" />
                  {pendingUser === userName && <span className="visually-hidden"> (entrando…)</span>}
                </button>
              </Form>
            </li>
          ))}
        </ul>

        <Form method="post" action={action} className="login__other" onSubmit={holdWhileHeld}>
          <label htmlFor="other-user">Otra persona del equipo</label>
          <div className="login__other-row">
            <input id="other-user" name="userName" className="field" placeholder="p. ej. nuria.paredes" autoComplete="username" spellCheck={false} required />
            <button type="submit" className="button button--ghost" aria-disabled={held}>
              Entrar
            </button>
          </div>
        </Form>
      </main>
    </div>
  )
}
