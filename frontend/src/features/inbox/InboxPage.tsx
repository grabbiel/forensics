import { SortDescending, Tray } from '@phosphor-icons/react'
import { Fragment, useRef } from 'react'
import { useLoaderData, useNavigation } from 'react-router'
import { EVIDENCE_TYPES, type EvidenceSummary, type EvidenceType } from '../../api/evidence'
import { TypeBadge } from '../../components/TypeBadge'
import { describeEmpty, describeResults, evidenceCount, formatUtcDate } from '../../lib/format'
import { usePendingSearch, useUpdateSearch } from '../../lib/useUpdateSearch'
import { readFilter, type inboxLoader } from './inboxLoader'

const TYPE_TABS: { type: EvidenceType | undefined; label: string }[] = [
  { type: undefined, label: 'Todos' },
  ...EVIDENCE_TYPES.map((type) => ({ type, label: type })),
]

/** Evidence inbox: Naver-style title and type tabs, summary bar, table, and loading/empty states. */
export function InboxPage() {
  const { rows, filter } = useLoaderData<typeof inboxLoader>()
  const loading = useNavigation().state === 'loading'
  const updateSearch = useUpdateSearch()
  // Tabs reflect the click at once, not when the fetch returns.
  const activeType = readFilter(new URLSearchParams(usePendingSearch())).type
  const titleRef = useRef<HTMLHeadingElement>(null)

  /** Sets or clears the `type` filter (a new history entry, so Back undoes it). */
  function selectType(type: EvidenceType | undefined) {
    if (type === activeType) return
    updateSearch((params) => (type ? params.set('type', type) : params.delete('type')))
  }

  /** Drops every filter; focus moves to the heading because the button disappears. */
  function clearFilters() {
    updateSearch((params) => {
      params.delete('q')
      params.delete('type')
    })
    titleRef.current?.focus()
  }

  return (
    <section className="panel" aria-labelledby="inbox-title">
      {loading && <div className="progress" aria-hidden="true" />}

      <div className="panel__head">
        <h1 id="inbox-title" className="panel__title" ref={titleRef} tabIndex={-1}>
          Bandeja de evidencias
        </h1>
        <div className="tabs" role="group" aria-label="Filtrar por tipo">
          {TYPE_TABS.map(({ type, label }, index) => (
            <Fragment key={label}>
              {index > 0 && (
                <span className="tabs__sep" aria-hidden="true">
                  /
                </span>
              )}
              <button type="button" className="tab" aria-pressed={activeType === type} onClick={() => selectType(type)}>
                {label}
              </button>
            </Fragment>
          ))}
        </div>
      </div>

      <div className="subbar">
        <p className="subbar__count" aria-hidden="true">
          {loading ? 'Cargando…' : <strong>{evidenceCount(rows.length)}</strong>}
          {!loading && filter.q && <> para «{filter.q}»</>}
        </p>
        {/* Announces only settled results, never the intermediate "loading". */}
        <p className="visually-hidden" role="status">
          {loading ? '' : describeResults(rows.length, filter)}
        </p>
        <span className="subbar__sort">
          <SortDescending size={16} aria-hidden="true" />
          Más recientes primero
        </span>
      </div>

      <div className="results" aria-busy={loading}>
        {rows.length === 0 ? (
          <EmptyState message={describeEmpty(filter)} canClear={Boolean(filter.q || filter.type)} onClear={clearFilters} />
        ) : (
          <EvidenceTable rows={rows} />
        )}
      </div>
    </section>
  )
}

/**
 * Semantic table: Naver-dense rows on wide screens, Bilibili-style feed cards on phones.
 * Explicit roles keep table semantics in Safari once CSS changes the display type.
 */
function EvidenceTable({ rows }: { rows: EvidenceSummary[] }) {
  return (
    <div className="table-wrap" role="region" aria-label="Tabla de evidencias" tabIndex={0}>
      <table className="table" role="table">
        <caption className="visually-hidden">Evidencias, más recientes primero</caption>
        <thead role="rowgroup">
          <tr role="row">
            <th scope="col" role="columnheader">
              Código
            </th>
            <th scope="col" role="columnheader">
              Tipo
            </th>
            <th scope="col" role="columnheader">
              Descripción
            </th>
            <th scope="col" role="columnheader" className="table__num">
              Registrada
            </th>
          </tr>
        </thead>
        <tbody role="rowgroup">
          {rows.map((row) => (
            <tr key={row.code} role="row">
              <th scope="row" role="rowheader" className="table__code">
                {row.code}
              </th>
              <td role="cell" className="table__type">
                <TypeBadge type={row.typeCode} />
              </td>
              <td role="cell" className="table__desc">
                {row.description}
              </td>
              <td role="cell" className="table__num">
                <time dateTime={row.registeredOn}>{formatUtcDate(row.registeredOn)}</time>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** Explains why the list is empty and offers the way out. */
function EmptyState({ message, canClear, onClear }: { message: string; canClear: boolean; onClear: () => void }) {
  return (
    <div className="empty">
      <Tray size={40} aria-hidden="true" />
      <p className="empty__title">{message}</p>
      {canClear && (
        <button type="button" className="button button--ghost" onClick={onClear}>
          Quitar filtros
        </button>
      )}
    </div>
  )
}
