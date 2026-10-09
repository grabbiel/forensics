import { useEffect, useRef } from 'react'
import { isRouteErrorResponse, useRevalidator, useRouteError } from 'react-router'
import { ApiError } from '../api/client'

/** Route error boundary: shows the API's problem details and offers a retry. */
export function RouteError() {
  const error = useRouteError()
  const revalidator = useRevalidator()
  const retrying = revalidator.state === 'loading'
  const { title, detail } = describe(error)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const wasRetrying = useRef(false)

  // Still mounted after a retry means it failed again: move focus so it is announced.
  useEffect(() => {
    if (wasRetrying.current && !retrying) headingRef.current?.focus()
    wasRetrying.current = retrying
  }, [retrying])

  return (
    <section className="panel panel--message panel--error" role="alert" aria-labelledby="error-title">
      <h1 id="error-title" className="panel__title" ref={headingRef} tabIndex={-1}>
        {title}
      </h1>
      <p className="panel__hint">{detail}</p>
      {/* aria-disabled, not disabled, so keyboard focus stays on the button while retrying. */}
      <button type="button" className="button" aria-disabled={retrying} onClick={() => retrying || revalidator.revalidate()}>
        {retrying ? 'Reintentando…' : 'Reintentar'}
      </button>
    </section>
  )
}

/** Turns any thrown value into user-facing text. */
function describe(error: unknown): { title: string; detail: string } {
  if (error instanceof ApiError) {
    return {
      title: error.problem?.title ?? 'No se pudo cargar la información',
      detail: error.problem?.detail ?? `El servidor respondió con el código ${error.status}.`,
    }
  }
  if (isRouteErrorResponse(error)) return { title: `Error ${error.status}`, detail: 'No se pudo completar la solicitud.' }
  if (error instanceof TypeError) return { title: 'Sin conexión con el servidor', detail: 'Comprueba tu red e inténtalo de nuevo.' }
  return { title: 'Algo salió mal', detail: 'Inténtalo de nuevo en unos segundos.' }
}
