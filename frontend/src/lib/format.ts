import type { Anomaly, ChainEvent, EvidenceFilter, IntegrityStatus } from '../api/evidence'

const dateTimeFormat = new Intl.DateTimeFormat('es', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
  timeZone: 'UTC',
})

/** Formats a UTC instant, e.g. "07 oct 2026, 14:03 UTC"; custody times are always shown in UTC. */
export function formatUtcDateTime(iso: string): string {
  return `${dateTimeFormat.format(new Date(iso))} UTC`
}

/** "1 evidencia" / "5 evidencias". */
export function evidenceCount(count: number): string {
  return `${count} ${count === 1 ? 'evidencia' : 'evidencias'}`
}

export const INTEGRITY_LABELS: Record<IntegrityStatus, string> = { Unverified: 'Sin verificar', Valid: 'Íntegra', Invalid: 'Alterada' }

export const ANOMALY_LABELS: Record<Anomaly['kind'], string> = { Overdue: 'Transferencia vencida', AcceptedLate: 'Aceptada fuera de plazo' }

export const SEVERITY_LABELS: Record<Anomaly['severity'], string> = { Medium: 'Severidad media', High: 'Severidad alta' }

/**
 * Result summary for screen readers, e.g. "3 evidencias de tipo LOG en custodia de Diego Salas para «vpn»; hay más".
 */
export function describeResults(count: number, filter: EvidenceFilter, context: { custodian?: string; more?: boolean } = {}): string {
  const parts = [
    evidenceCount(count),
    filter.type && `de tipo ${filter.type}`,
    context.custodian && `en custodia de ${context.custodian}`,
    filter.status && `con integridad «${INTEGRITY_LABELS[filter.status]}»`,
    filter.q && `para «${filter.q}»`,
  ]
  return parts.filter(Boolean).join(' ') + (context.more ? '; hay más en la página siguiente' : '')
}

/** Why the list is empty, in the user's terms. */
export function describeEmpty(filter: EvidenceFilter): string {
  if (filter.q) return `Sin resultados para «${filter.q}»${filter.type ? ` en ${filter.type}` : ''}.`
  if (filter.custodianId || filter.status) return 'Ninguna evidencia coincide con los filtros.'
  if (filter.type) return `No hay evidencias de tipo ${filter.type}.`
  return 'Aún no hay evidencias registradas.'
}

const sizeFormat = new Intl.NumberFormat('es', { maximumFractionDigits: 1 })

/** "512 B", "2 KB", "1,5 MB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${sizeFormat.format(bytes / 1024)} KB`
  return `${sizeFormat.format(bytes / (1024 * 1024))} MB`
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
