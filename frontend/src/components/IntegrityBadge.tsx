import { CheckCircle, Question, WarningOctagon, type Icon } from '@phosphor-icons/react'
import type { IntegrityStatus } from '../api/evidence'
import { INTEGRITY_LABELS } from '../lib/format'

const ICONS: Record<IntegrityStatus, Icon> = { Unverified: Question, Valid: CheckCircle, Invalid: WarningOctagon }

/** The last recorded verification, as icon plus words: colour is never the only signal. */
export function IntegrityBadge({ status }: { status: IntegrityStatus }) {
  const StatusIcon = ICONS[status]
  return (
    <span className={`integrity integrity--${status.toLowerCase()}`}>
      <StatusIcon size={14} weight="bold" aria-hidden="true" />
      {INTEGRITY_LABELS[status]}
    </span>
  )
}
