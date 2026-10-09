import { getJson } from './client'

export const EVIDENCE_TYPES = ['LOG', 'CSV', 'EML'] as const
export type EvidenceType = (typeof EVIDENCE_TYPES)[number]

export const INTEGRITY_STATUSES = ['Unverified', 'Valid', 'Invalid'] as const
export type IntegrityStatus = (typeof INTEGRITY_STATUSES)[number]

export type InboxSort = 'lastEventAt:desc' | 'lastEventAt:asc'

/** A user as the API names them. */
export interface PersonRef {
  id: number
  displayName: string
}

/** One inbox row. */
export interface EvidenceSummary {
  code: string
  typeCode: EvidenceType
  description: string
  currentCustodian: PersonRef
  lastEventAtUtc: string
  eventCount: number
  integrityStatus: IntegrityStatus
  integrityCheckedAtUtc: string | null
  pendingTransfer: { transferId: number; toCustodianId: number; sinceUtc: string } | null
}

export interface InboxPage {
  items: EvidenceSummary[]
  /** Opaque; continues this same listing only. Null on the last page. */
  nextCursor: string | null
}

export interface EvidenceFilter {
  q?: string
  type?: EvidenceType
  custodianId?: number
  status?: IntegrityStatus
  sort?: InboxSort
  cursor?: string
}

export type TransferStatus = 'Pending' | 'Accepted' | 'Rejected'

/** A transfer as the detail shows it, with the ETag that accept and reject send as If-Match. */
export interface TransferView {
  transferId: number
  status: TransferStatus
  from: PersonRef
  to: PersonRef
  requestedBy: PersonRef
  requestedAtUtc: string
  reason: string
  etag: string
}

export interface Anomaly {
  transferId: number
  kind: 'Overdue' | 'AcceptedLate'
  severity: 'Medium' | 'High'
  requestedAtUtc: string
  elapsedSeconds: number
  deadlineSeconds: number
  explanation: string
}

export interface EvidenceDetail {
  code: string
  typeCode: EvidenceType
  description: string
  capturedAtUtc: string
  registeredAtUtc: string
  registeredBy: PersonRef
  initialCustodian: PersonRef
  currentCustodian: PersonRef
  eventCount: number
  lastEventAtUtc: string
  content: { sha256: string; byteLength: number; mediaType: string }
  integrity: { status: IntegrityStatus; checkedAtUtc: string | null; checkedThroughSeq: number | null }
  pendingTransfer: TransferView | null
  anomalies: Anomaly[]
}

export type CustodyEventKind = 'EvidenceRegistered' | 'TransferRequested' | 'TransferAccepted' | 'TransferRejected'

export interface ChainEvent {
  eventId: number
  seq: number
  kind: CustodyEventKind
  occurredAtUtc: string
  actor: PersonRef
  from: PersonRef | null
  to: PersonRef | null
  transferId: number | null
  notes: string
  keyId: string
  mac: string
  prevMac: string | null
}

export interface EvidenceChain {
  code: string
  events: ChainEvent[]
}

export interface VerificationReport {
  code: string
  valid: boolean
  verifiedThroughSeq: number
  eventCount: number
  checkedAtUtc: string
  firstInvalid: { eventId: number | null; seq: number; reason: string; detail: string } | null
}

/** True for LOG, CSV or EML. */
export function isEvidenceType(value: string | null | undefined): value is EvidenceType {
  return EVIDENCE_TYPES.includes(value as EvidenceType)
}

/** True for Unverified, Valid or Invalid. */
export function isIntegrityStatus(value: string | null | undefined): value is IntegrityStatus {
  return INTEGRITY_STATUSES.includes(value as IntegrityStatus)
}

/** Fetches one inbox page for the given filter. */
export function listEvidence(filter: EvidenceFilter, signal?: AbortSignal): Promise<InboxPage> {
  const params = new URLSearchParams()
  if (filter.q) params.set('q', filter.q)
  if (filter.type) params.set('type', filter.type)
  if (filter.custodianId) params.set('custodianId', String(filter.custodianId))
  if (filter.status) params.set('status', filter.status)
  if (filter.sort) params.set('sort', filter.sort)
  if (filter.cursor) params.set('cursor', filter.cursor)
  const query = params.size > 0 ? `?${params}` : ''
  return getJson<InboxPage>(`/api/v1/evidence${query}`, signal)
}

const evidencePath = (code: string) => `/api/v1/evidence/${encodeURIComponent(code)}`

export function getEvidence(code: string, signal?: AbortSignal): Promise<EvidenceDetail> {
  return getJson<EvidenceDetail>(evidencePath(code), signal)
}

export function getChain(code: string, signal?: AbortSignal): Promise<EvidenceChain> {
  return getJson<EvidenceChain>(`${evidencePath(code)}/chain`, signal)
}

/** Verifies the chain now; the server also records the result as the evidence's integrity status. */
export function verifyChain(code: string, signal?: AbortSignal): Promise<VerificationReport> {
  return getJson<VerificationReport>(`${evidencePath(code)}/chain/verify`, signal)
}
