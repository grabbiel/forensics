import { LinkSimple, SignOut } from '@phosphor-icons/react'
import { Form, Link, Outlet } from 'react-router'
import { getSession } from '../auth/session'
import { SearchBox } from './SearchBox'

/** Page frame: skip link, sticky header with the search and who is signed in, main content, footer. */
export function AppShell() {
  // Loaders send anyone signed out to /login, so a user is here whenever a page renders.
  const user = getSession()?.user
  return (
    <>
      <a className="skip-link" href="#main">
        Saltar al contenido
      </a>
      <header className="topbar">
        <div className="topbar__inner">
          <Link to="/" className="brand" aria-label="Evidence Chain, inicio">
            <span className="brand__mark">
              <LinkSimple size={18} weight="bold" aria-hidden="true" />
            </span>
            <span className="brand__name">Evidence Chain</span>
          </Link>
          <SearchBox />
          {user && (
            <div className="account">
              <span className="account__who">
                <span className="account__name">{user.displayName}</span>
                <span className="account__role">{user.role}</span>
              </span>
              <Form method="post" action="/logout">
                <button type="submit" className="icon-button" aria-label={`Cerrar sesión de ${user.displayName}`} title="Cerrar sesión">
                  <SignOut size={18} aria-hidden="true" />
                </button>
              </Form>
            </div>
          )}
        </div>
      </header>
      <main id="main" className="page" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="footer">Evidence Chain · cadena de custodia de evidencia digital</footer>
    </>
  )
}

/** Skeleton of a detail page: heading, facts, timeline. */
export function DetailLoading() {
  return (
    <section className="panel" aria-busy="true" aria-label="Cargando evidencia">
      <div className="panel__head">
        <span className="skeleton" style={{ width: 200, height: 20 }} />
      </div>
      <div className="skeleton-rows" role="status">
        <span className="visually-hidden">Cargando evidencia…</span>
        {Array.from({ length: 4 }, (_, i) => (
          <span key={i} className="skeleton" style={{ width: `${88 - i * 12}%` }} />
        ))}
      </div>
    </section>
  )
}

/** Skeleton in the shape of the inbox, shown while the first load runs. */
export function PageLoading() {
  return (
    <section className="panel" aria-busy="true" aria-label="Cargando evidencias">
      <div className="panel__head">
        <span className="skeleton" style={{ width: 168, height: 20 }} />
        <span className="skeleton" style={{ width: 150, height: 18 }} />
      </div>
      <div className="subbar">
        <span className="skeleton" style={{ width: 96, height: 14 }} />
      </div>
      <div className="skeleton-rows" role="status">
        <span className="visually-hidden">Cargando evidencias…</span>
        {Array.from({ length: 5 }, (_, i) => (
          <div key={i} className="skeleton-row">
            <span className="skeleton" style={{ width: 128 }} />
            <span className="skeleton" style={{ width: 44 }} />
            <span className="skeleton" style={{ width: `${72 - i * 7}%` }} />
            <span className="skeleton" style={{ width: 72 }} />
          </div>
        ))}
      </div>
    </section>
  )
}
