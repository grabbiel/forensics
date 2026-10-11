import type { NotificationItem, NotificationKind } from '../../api/notifications'
import type { SessionUser } from '../../auth/session'

/**
 * Why this reader got the notification. It comes from the item's parties and the reader's id; anyone else is told as
 * a Supervisor. The server's recipient rule decides *who* gets told; this only decides *how it reads*.
 */
export type Stance = 'requester' | 'recipient' | 'holder' | 'supervisor'

/** A run of the sentence: plain text, or a name or code shown in bold. */
export type Part = string | { strong: string }

export interface Sentence {
  parts: Part[]
  /** The request's reason, shown under the sentence for request notifications; rejections carry theirs inline. */
  detail?: string
}

/** The reader's stance: the requester, then the recipient, then the holder; anyone else reads as a Supervisor. */
export function stanceOf(item: NotificationItem, viewer: SessionUser): Stance {
  if (item.requestedBy.id === viewer.id) return 'requester'
  if (item.to.id === viewer.id) return 'recipient'
  if (item.from.id === viewer.id) return 'holder'
  return 'supervisor'
}

type Template = (item: NotificationItem) => Part[]

const motive = (item: NotificationItem): Part[] => (item.decisionNotes ? [` Motivo: ${item.decisionNotes}`] : [])

/**
 * The copy table: one sentence per stance and kind the server's rule can produce. A pair outside it (a Supervisor who
 * is also the holder, told of a decision) reads as the Supervisor row.
 */
export const COPY: {
  requester: Record<'TransferAccepted' | 'TransferRejected', Template>
  recipient: Record<'TransferRequested', Template>
  holder: Record<'TransferRequested', Template>
  supervisor: Record<NotificationKind, Template>
} = {
  requester: {
    TransferAccepted: (i) => [{ strong: i.to.displayName }, ' aceptó tu solicitud: ', { strong: i.evidenceCode }, ' pasa a su custodia.'],
    TransferRejected: (i) => [{ strong: i.to.displayName }, ' rechazó tu solicitud de transferir ', { strong: i.evidenceCode }, '.', ...motive(i)],
  },
  recipient: {
    TransferRequested: (i) => [{ strong: i.requestedBy.displayName }, ' solicitó transferirte ', { strong: i.evidenceCode }, ', ahora en custodia de ', { strong: i.from.displayName }, '.'],
  },
  holder: {
    TransferRequested: (i) => [{ strong: i.requestedBy.displayName }, ' solicitó que entregues ', { strong: i.evidenceCode }, ' a ', { strong: i.to.displayName }, '.'],
  },
  supervisor: {
    TransferRequested: (i) => [{ strong: i.requestedBy.displayName }, ' solicitó transferir ', { strong: i.evidenceCode }, ' de ', { strong: i.from.displayName }, ' a ', { strong: i.to.displayName }, '.'],
    TransferAccepted: (i) => [{ strong: i.to.displayName }, ' aceptó la transferencia de ', { strong: i.evidenceCode }, ' solicitada por ', { strong: i.requestedBy.displayName }, '.'],
    TransferRejected: (i) => [{ strong: i.to.displayName }, ' rechazó la transferencia de ', { strong: i.evidenceCode }, ' solicitada por ', { strong: i.requestedBy.displayName }, '.', ...motive(i)],
  },
}

/** The reader's sentence for an item. Pure, never throws, and needs no other data. */
export function sentenceFor(item: NotificationItem, viewer: SessionUser): Sentence {
  const row: Partial<Record<NotificationKind, Template>> = COPY[stanceOf(item, viewer)]
  const template = row[item.kind] ?? COPY.supervisor[item.kind]
  return { parts: template(item), detail: item.kind === 'TransferRequested' ? item.reason : undefined }
}

/** Flattens parts to plain text, for accessible names and tests. */
export function sentenceText(parts: Part[]): string {
  return parts.map((part) => (typeof part === 'string' ? part : part.strong)).join('')
}
