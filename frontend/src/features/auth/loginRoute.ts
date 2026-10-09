import { data, redirect, type ActionFunctionArgs, type LoaderFunctionArgs } from 'react-router'
import { signIn } from '../../api/auth'
import { ApiError } from '../../api/client'
import { forgetPeople } from '../../api/people'
import { safeRedirect } from '../../auth/guard'
import { clearSession, getSession, saveSession } from '../../auth/session'
import { clearAllIntents } from '../transfers/pendingIntent'

/** Already signed in: go where the user was headed. */
export function loginLoader({ request }: LoaderFunctionArgs) {
  if (getSession()) throw redirect(safeRedirect(new URL(request.url).searchParams.get('redirectTo')))
  return null
}

/** Signs in as the chosen demo user and returns to where the user was headed. */
export async function loginAction({ request }: ActionFunctionArgs) {
  const form = await request.formData()
  const userName = String(form.get('userName') ?? '').trim()
  if (!userName) return data({ error: 'Indica un usuario.' }, { status: 400 })

  try {
    saveSession(await signIn(userName, request.signal))
  } catch (error) {
    if (error instanceof ApiError && error.status === 400) return data({ error: `No hay ningún usuario «${userName}».` }, { status: 400 })
    if (error instanceof ApiError) return data({ error: 'El servidor no pudo iniciar la sesión. Inténtalo de nuevo.' }, { status: 502 })
    return data({ error: 'Sin conexión con el servidor. Comprueba tu red e inténtalo de nuevo.' }, { status: 503 })
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
