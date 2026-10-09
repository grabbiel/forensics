import { useEffect, useRef } from 'react'
import { isRouteErrorResponse, Link, useRevalidator, useRouteError } from 'react-router'
import { ApiError } from '../api/client'

/** What a route calls a 404, e.g. a missing evidence; other routes say the page is not there. */
interface NotFoundText {
  title: string
  detail: string
}

const PAGE_NOT_FOUND: NotFoundText = { title: 'No encontrada', detail: 'Lo que buscas no existe o ya no está disponible.' }

/** Route error boundary: explains the failure and offers a retry when one can help. */
export function RouteError({ notFound = PAGE_NOT_FOUND }: { notFound?: NotFoundText }) {
  const error = useRouteError()
  const revalidator = useRevalidator()
  const retrying = revalidator.state === 'loading'
  const { title, detail, retry } = describe(error, notFound)
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
      {retry ? (
        // aria-disabled, not disabled, so keyboard focus stays on the button while retrying.
        <button type="button" className="button" aria-disabled={retrying} onClick={() => retrying || revalidator.revalidate()}>
          {retrying ? 'Reintentando…' : 'Reintentar'}
        </button>
      ) : (
        <Link to="/" className="button">
          Ir a la bandeja
        </Link>
      )}
    </section>
  )
}

/**
 * Turns any thrown value into Spanish text, never the API's own (English) detail. Retrying only helps when the
 * failure may pass; a missing evidence or a refused role gets a way back instead.
 */
function describe(error: unknown, notFound: NotFoundText): { title: string; detail: string; retry: boolean } {
  if (error instanceof ApiError && error.status === 404) return { ...notFound, retry: false }
  if (error instanceof ApiError && error.status === 403) {
    return { title: 'Sin permiso', detail: 'Tu rol no permite ver esto.', retry: false }
  }
  if (error instanceof ApiError) {
    return { title: 'No se pudo cargar la información', detail: `El servidor respondió con el código ${error.status}.`, retry: true }
  }
  if (isRouteErrorResponse(error)) return { title: `Error ${error.status}`, detail: 'No se pudo completar la solicitud.', retry: true }
  // A failed fetch, or a read that timed out.
  if (error instanceof TypeError || (error instanceof DOMException && error.name === 'TimeoutError'))
    return { title: 'Sin conexión con el servidor', detail: 'Comprueba tu red e inténtalo de nuevo.', retry: true }
  return { title: 'Algo salió mal', detail: 'Inténtalo de nuevo en unos segundos.', retry: true }
}
