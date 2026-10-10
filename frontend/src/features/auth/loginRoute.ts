import { data, redirect, type ActionFunctionArgs, type LoaderFunctionArgs } from 'react-router'
import { signIn } from '../../api/auth'
import { ApiError, DEFAULT_RETRY_AFTER_SECONDS } from '../../api/client'
import { forgetPeople } from '../../api/people'
import { safeRedirect } from '../../auth/guard'
import { clearSession, getSession, saveSession } from '../../auth/session'
import { waitSentence } from '../../lib/useWaited'
import { clearAllIntents } from '../transfers/pendingIntent'

/** Already signed in: go where the user was headed. */
export function loginLoader({ request }: LoaderFunctionArgs) {
  if (getSession()) throw redirect(safeRedirect(new URL(request.url).searchParams.get('redirectTo')))
  return null
}

/** Why a sign-in failed, and after a 429 the seconds before trying again. */
interface LoginFailure {
  error: string
  retryAfterSeconds?: number
}

/** Signs in as the chosen demo user and returns to where the user was headed. */
export async function loginAction({ request }: ActionFunctionArgs) {
  const form = await request.formData()
  const userName = String(form.get('userName') ?? '').trim()
  if (!userName) return data<LoginFailure>({ error: 'Indica un usuario.' }, { status: 400 })

  try {
    saveSession(await signIn(userName, request.signal))
  } catch (error) {
    if (error instanceof ApiError && error.status === 400) return data<LoginFailure>({ error: `No hay ningún usuario «${userName}».` }, { status: 400 })
    if (error instanceof ApiError && error.status === 429) {
      const wait = error.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS
      return data<LoginFailure>({ error: `Demasiados intentos de inicio de sesión seguidos. ${waitSentence(wait)}`, retryAfterSeconds: wait }, { status: 429 })
    }
    if (error instanceof ApiError) return data<LoginFailure>({ error: 'El servidor no pudo iniciar la sesión. Inténtalo de nuevo.' }, { status: 502 })
    return data<LoginFailure>({ error: 'Sin conexión con el servidor. Comprueba tu red e inténtalo de nuevo.' }, { status: 503 })
  }
  return redirect(safeRedirect(new URL(request.url).searchParams.get('redirectTo')))
}

/** Forgets the token. */
export function logoutAction() {
  clearSession()
  forgetPeople()
  clearAllIntents()
  return redirect('/login')
}
