import type { ChainEvent, EvidenceFilter } from '../api/evidence'

const dateFormat = new Intl.DateTimeFormat('es', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' })
const dateTimeFormat = new Intl.DateTimeFormat('es', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: 'UTC',
})

/** Formats a UTC instant's date for display, e.g. "07 oct 2026". */
export function formatUtcDate(iso: string): string {
  return dateFormat.format(new Date(iso.length === 10 ? `${iso}T00:00:00Z` : iso))
}

/** Formats a UTC instant, e.g. "07 oct 2026, 14:03 UTC"; custody times are always shown in UTC. */
export function formatUtcDateTime(iso: string): string {
  return `${dateTimeFormat.format(new Date(iso))} UTC`
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

/** One custody event in a sentence, e.g. "Lucía Ferrer pidió pasarla de Diego Salas a Nuria Paredes". */
export function describeEvent(event: ChainEvent): string {
  const from = event.from?.displayName ?? '—'
  const to = event.to?.displayName ?? '—'
  switch (event.kind) {
    case 'EvidenceRegistered':
      return `${event.actor.displayName} la registró; custodia inicial: ${to}`
    case 'TransferRequested':
      return `${event.actor.displayName} pidió pasarla de ${from} a ${to}`
    case 'TransferAccepted':
      return `${event.actor.displayName} aceptó la custodia`
    case 'TransferRejected':
      return `${event.actor.displayName} rechazó la transferencia; sigue con ${from}`
  }
}
