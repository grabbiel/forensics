import type { EvidenceFilter } from '../api/evidence'

const dateFormat = new Intl.DateTimeFormat('es', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' })

/** Formats a yyyy-MM-dd UTC date for display, e.g. "07 oct 2026". */
export function formatUtcDate(isoDate: string): string {
  return dateFormat.format(new Date(`${isoDate}T00:00:00Z`))
}

/** "1 evidencia" / "5 evidencias". */
export function evidenceCount(count: number): string {
  return `${count} ${count === 1 ? 'evidencia' : 'evidencias'}`
}

/** Result summary for screen readers, e.g. "3 evidencias de tipo LOG para «firewall»". */
export function describeResults(count: number, filter: EvidenceFilter): string {
  return [evidenceCount(count), filter.type && `de tipo ${filter.type}`, filter.q && `para «${filter.q}»`].filter(Boolean).join(' ')
}

/** Why the list is empty, in the user's terms. */
export function describeEmpty(filter: EvidenceFilter): string {
  if (filter.q) return `Sin resultados para «${filter.q}»${filter.type ? ` en ${filter.type}` : ''}.`
  if (filter.type) return `No hay evidencias de tipo ${filter.type}.`
  return 'Aún no hay evidencias registradas.'
}
