import { postJson, type Sent } from './client'
import type { PersonRef, TransferStatus } from './evidence'

/** A transfer as the custody-transfer endpoints return it. */
export interface TransferResource {
  transferId: number
  evidenceCode: string
  status: TransferStatus
  from: PersonRef
  to: PersonRef
  requestedBy: PersonRef
  requestedAtUtc: string
  reason: string
  decidedBy: PersonRef | null
  decidedAtUtc: string | null
  decisionNotes: string | null
  etag: string
}

interface TransferRequest {
  evidenceCode: string
  toCustodianId: number
  reason: string
}

export type Decision = 'accept' | 'reject'

/** Asks for a transfer. The same key resends safely: the server answers with the transfer it already made. */
export function requestTransfer(request: TransferRequest, idempotencyKey: string, signal?: AbortSignal): Promise<Sent<TransferResource>> {
  return postJson<TransferResource>('/api/v1/custody-transfers', request, { signal, headers: { 'Idempotency-Key': idempotencyKey } })
}

/** Accepts or rejects at the version the user saw (If-Match); a newer version answers 409 with what happened. */
export function decideTransfer(
  transferId: number,
  decision: Decision,
  body: { notes?: string; reason?: string },
  { idempotencyKey, etag }: { idempotencyKey: string; etag: string },
  signal?: AbortSignal,
): Promise<Sent<TransferResource>> {
  return postJson<TransferResource>(`/api/v1/custody-transfers/${transferId}/${decision}`, body, {
    signal,
    headers: { 'Idempotency-Key': idempotencyKey, 'If-Match': etag },
  })
}
