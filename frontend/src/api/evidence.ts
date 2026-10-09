import { getJson } from './client'

export const EVIDENCE_TYPES = ['LOG', 'CSV', 'EML'] as const
export type EvidenceType = (typeof EVIDENCE_TYPES)[number]

/** One inbox row (tracer shape; custodian, last event and integrity arrive on Day 3). */
export interface EvidenceSummary {
  code: string
  typeCode: EvidenceType
  registeredOn: string // yyyy-MM-dd, UTC
  description: string
}

export interface EvidenceFilter {
  q?: string
  type?: EvidenceType
}

/** True for LOG, CSV or EML. */
export function isEvidenceType(value: string | null | undefined): value is EvidenceType {
  return EVIDENCE_TYPES.includes(value as EvidenceType)
}

/** Fetches the inbox for the given filter. */
export function listEvidence(filter: EvidenceFilter, signal?: AbortSignal): Promise<EvidenceSummary[]> {
  const params = new URLSearchParams()
  if (filter.q) params.set('q', filter.q)
  if (filter.type) params.set('type', filter.type)
  const query = params.size > 0 ? `?${params}` : ''
  return getJson<EvidenceSummary[]>(`/api/v1/evidence${query}`, signal)
}
