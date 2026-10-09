import { ArrowDown, ArrowUp, CaretRight, Tray } from '@phosphor-icons/react'
import { Fragment, useRef, type ChangeEvent } from 'react'
import { Link, useLoaderData, useNavigation } from 'react-router'
import { EVIDENCE_TYPES, INTEGRITY_STATUSES, type EvidenceSummary, type EvidenceType } from '../../api/evidence'
import type { Person } from '../../api/people'
import { IntegrityBadge } from '../../components/IntegrityBadge'
import { TypeBadge } from '../../components/TypeBadge'
import { describeEmpty, describeResults, evidenceCount, formatUtcDateTime, INTEGRITY_LABELS } from '../../lib/format'
import { usePendingSearch, useUpdateSearch } from '../../lib/useUpdateSearch'
import { readFilter, type inboxLoader } from './inboxLoader'

const TYPE_TABS: { type: EvidenceType | undefined; label: string }[] = [
  { type: undefined, label: 'Todos' },
  ...EVIDENCE_TYPES.map((type) => ({ type, label: type })),
]

const FILTER_PARAMS = ['q', 'type', 'custodianId', 'status'] as const

/**
 * Evidence inbox. Every filter, the sort and the page live in the URL; any change of filter or sort starts again
 * from the first page, because a cursor only continues the listing it came from.
 */
export function InboxPage() {
  const { rows, nextCursor, filter, custodians } = useLoaderData<typeof inboxLoader>()
  const loading = useNavigation().state === 'loading'
  const updateSearch = useUpdateSearch()
  // Controls reflect a click at once, not when the fetch returns.
  const pending = readFilter(new URLSearchParams(usePendingSearch()))
  const titleRef = useRef<HTMLHeadingElement>(null)
  const custodianName = (id: number) => custodians.find((c) => c.id === id)?.displayName

  /** Sets or clears one filter as a new history entry (Back undoes it), back on the first page. */
  function setFilter(name: string, value: string | undefined) {
    updateSearch((params) => {
      if (value) params.set(name, value)
      else params.delete(name)
      params.delete('cursor')
    })
  }

  function selectType(type: EvidenceType | undefined) {
    if (type !== pending.type) setFilter('type', type)
  }

  function toggleSort() {
    setFilter('sort', pending.sort ? undefined : 'lastEventAt:asc')
  }

  /** Moves between pages; focus goes to the heading because the clicked button may not exist on the new page. */
  function goToPage(cursor: string | undefined) {
    updateSearch((params) => (cursor ? params.set('cursor', cursor) : params.delete('cursor')))
    titleRef.current?.focus()
  }

  /** Drops every filter but keeps the sort; focus moves to the heading because the button disappears. */
  function clearFilters() {
    updateSearch((params) => {
      for (const name of [...FILTER_PARAMS, 'cursor']) params.delete(name)
    })
    titleRef.current?.focus()
  }

  const filtered = FILTER_PARAMS.some((name) => filter[name] !== undefined)
  const oldestFirst = pending.sort === 'lastEventAt:asc'

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
              <button type="button" className="tab" aria-pressed={pending.type === type} onClick={() => selectType(type)}>
                {label}
              </button>
            </Fragment>
          ))}
        </div>
      </div>

      <div className="filters">
        <Select
          label="Custodio"
          value={pending.custodianId ? String(pending.custodianId) : ''}
          onChange={(value) => setFilter('custodianId', value)}
          options={[{ value: '', label: 'Todos' }, ...custodians.map((c: Person) => ({ value: String(c.id), label: c.displayName }))]}
        />
        <Select
          label="Integridad"
          value={pending.status ?? ''}
          onChange={(value) => setFilter('status', value)}
          options={[{ value: '', label: 'Todas' }, ...INTEGRITY_STATUSES.map((status) => ({ value: status, label: INTEGRITY_LABELS[status] }))]}
        />
      </div>

      <div className="subbar">
        <p className="subbar__count" aria-hidden="true">
          {loading ? 'Cargando…' : <strong>{evidenceCount(rows.length)}</strong>}
          {!loading && (filter.cursor || nextCursor) && <> en esta página</>}
          {!loading && filter.q && <> para «{filter.q}»</>}
        </p>
        {/* Announces only settled results, never the intermediate "loading". */}
        <p className="visually-hidden" role="status">
          {loading ? '' : describeResults(rows.length, filter, { custodian: filter.custodianId ? custodianName(filter.custodianId) : undefined, more: Boolean(nextCursor) })}
        </p>
        <span className="subbar__sort">{oldestFirst ? 'Más antiguos primero' : 'Más recientes primero'}</span>
      </div>

      <div className="results" aria-busy={loading}>
        {rows.length === 0 ? (
          <EmptyState message={describeEmpty(filter)} canClear={filtered} onClear={clearFilters} />
        ) : (
          <EvidenceTable rows={rows} oldestFirst={oldestFirst} onToggleSort={toggleSort} custodianName={custodianName} />
        )}
      </div>

      {(filter.cursor || nextCursor) && (
        <nav className="pager" aria-label="Páginas de la bandeja">
          {filter.cursor && (
            <button type="button" className="button button--ghost" onClick={() => goToPage(undefined)}>
              Primera página
            </button>
          )}
          {nextCursor && (
            <button type="button" className="button button--ghost" onClick={() => goToPage(nextCursor)}>
              Página siguiente
              <CaretRight size={16} aria-hidden="true" />
            </button>
          )}
        </nav>
      )}
    </section>
  )
}

/** A labelled native select that writes the URL as soon as it changes. */
function Select({ label, value, options, onChange }: { label: string; value: string; options: { value: string; label: string }[]; onChange: (value: string | undefined) => void }) {
  return (
    <label className="filter">
      <span className="filter__label">{label}</span>
      <select className="filter__select" value={value} onChange={(event: ChangeEvent<HTMLSelectElement>) => onChange(event.target.value || undefined)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  )
}

/**
 * Semantic table: Naver-dense rows on wide screens, Bilibili-style feed cards on phones. The sorted column's header
 * holds the sort button and says the order through aria-sort. Explicit roles keep table semantics in Safari once CSS
 * changes the display type.
 */
function EvidenceTable({
  rows,
  oldestFirst,
  onToggleSort,
  custodianName,
}: {
  rows: EvidenceSummary[]
  oldestFirst: boolean
  onToggleSort: () => void
  custodianName: (id: number) => string | undefined
}) {
  return (
    <div className="table-wrap" role="region" aria-label="Tabla de evidencias" tabIndex={0}>
      <table className="table" role="table">
        <caption className="visually-hidden">Evidencias por último evento de custodia, {oldestFirst ? 'más antiguos' : 'más recientes'} primero</caption>
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
            <th scope="col" role="columnheader">
              Custodio
            </th>
            <th scope="col" role="columnheader">
              Integridad
            </th>
            <th scope="col" role="columnheader" className="table__num" aria-sort={oldestFirst ? 'ascending' : 'descending'}>
              <button type="button" className="sort-button" onClick={onToggleSort}>
                Último evento
                {oldestFirst ? <ArrowUp size={14} aria-hidden="true" /> : <ArrowDown size={14} aria-hidden="true" />}
              </button>
            </th>
          </tr>
        </thead>
        <tbody role="rowgroup">
          {rows.map((row) => (
            <tr key={row.code} role="row">
              <th scope="row" role="rowheader" className="table__code">
                <Link to={`/evidence/${row.code}`}>{row.code}</Link>
              </th>
              <td role="cell" className="table__type">
                <TypeBadge type={row.typeCode} />
              </td>
              <td role="cell" className="table__desc">
                {row.description}
              </td>
              <td role="cell" className="table__custodian">
                {row.currentCustodian.displayName}
                {row.pendingTransfer && (
                  <span className="pending-hint">
                    Pendiente de pasar a {custodianName(row.pendingTransfer.toCustodianId) ?? 'otro custodio'}
                  </span>
                )}
              </td>
              <td role="cell" className="table__integrity">
                <IntegrityBadge status={row.integrityStatus} />
              </td>
              <td role="cell" className="table__num">
                <time dateTime={row.lastEventAtUtc}>{formatUtcDateTime(row.lastEventAtUtc)}</time>
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
