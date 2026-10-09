import { LinkSimple } from '@phosphor-icons/react'
import { Link, Outlet } from 'react-router'
import { SearchBox } from './SearchBox'

/** Page frame: skip link, sticky header with the search, main content, footer. */
export function AppShell() {
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
        </div>
      </header>
      <main id="main" className="page" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="footer">Evidence Chain · cadena de custodia de evidencia digital</footer>
    </>
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
