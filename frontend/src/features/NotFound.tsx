import { Link } from 'react-router'

/** Unknown URL, shown inside the app frame so search and navigation stay available. */
export function NotFound() {
  return (
    <section className="panel panel--message" aria-labelledby="not-found-title">
      <h1 id="not-found-title" className="panel__title">
        Página no encontrada
      </h1>
      <p className="panel__hint">Esta dirección no corresponde a ninguna sección de Evidence Chain.</p>
      <Link to="/" className="button">
        Ir a la bandeja
      </Link>
    </section>
  )
}
