import { EnvelopeSimple, FileCsv, TerminalWindow, type Icon } from '@phosphor-icons/react'
import type { EvidenceType } from '../api/evidence'

const TYPES: Record<EvidenceType, { label: string; icon: Icon }> = {
  LOG: { label: 'Registro de red', icon: TerminalWindow },
  CSV: { label: 'Exportación financiera', icon: FileCsv },
  EML: { label: 'Correo electrónico', icon: EnvelopeSimple },
}

/** Neutral Bilibili-style tag: icon plus type code; the full name is announced to screen readers. */
export function TypeBadge({ type }: { type: EvidenceType }) {
  const { label, icon: TypeIcon } = TYPES[type]
  return (
    <span className="tag" title={label}>
      <TypeIcon size={14} aria-hidden="true" />
      {type}
      <span className="visually-hidden"> ({label})</span>
    </span>
  )
}
