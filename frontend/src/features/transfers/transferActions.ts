import { data, type ActionFunctionArgs } from 'react-router'
import { ApiError, DEFAULT_RETRY_AFTER_SECONDS, withTimeout, type ProblemDetails } from '../../api/client'
import { decideTransfer, requestTransfer, type Decision, type TransferResource } from '../../api/transfers'
import { signedIn } from '../../auth/guard'
import { clearIntent, intentScope } from './pendingIntent'

/** How long a write may take before its outcome counts as unknown. */
const WRITE_TIMEOUT_MS = 15_000

export const PROBLEM = {
  inFlight: 'urn:evidence-chain:problem:idempotency-in-flight',
  concurrentWrite: 'urn:evidence-chain:problem:concurrent-write',
  staleVersion: 'urn:evidence-chain:problem:stale-version',
} as const

type WriteKind = 'request' | Decision

/**
 * What a transfer action answers, naming the write it was. Never thrown, so the page stays mounted and can explain it:
 * - done: the server did it (or had already done it, when it repeats an earlier request with the same key);
 * - refused: the server answered with a problem; a 409 carries the transfer's current state;
 * - throttled: a 429, sent before the write ran, so nothing was saved; the intent stays for a retry with the same
 *   key once the seconds it asks for have passed;
 * - unknown: no answer, a timeout, a server error, or the first send still running (status 0 when there was no
 *   answer at all). The server may or may not have done it, so the intent stays for a retry with the same key.
 */
export type WriteResult =
  | { outcome: 'done'; write: WriteKind; transfer: TransferResource; replayed: boolean }
  | { outcome: 'refused'; write: WriteKind; status: number; problem?: ProblemDetails }
  | { outcome: 'throttled'; write: WriteKind; retryAfterSeconds: number }
  | { outcome: 'unknown'; write: WriteKind; status: number; problem?: ProblemDetails }

/** POST /evidence/:id/transfer: form fields toCustodianId, reason, idempotencyKey. */
export async function requestTransferAction({ request, params }: ActionFunctionArgs) {
  const form = await request.formData()
  const code = params.id!
  return write(request, 'request', code, String(form.get('idempotencyKey') ?? ''), (key, signal) =>
    requestTransfer({ evidenceCode: code, toCustodianId: Number(form.get('toCustodianId')), reason: String(form.get('reason') ?? '') }, key, signal),
  )
}

/** POST /transfers/:transferId/accept or /reject: form fields evidenceCode, etag, idempotencyKey, and notes or reason. */
export function decisionAction(decision: Decision) {
  return async ({ request, params }: ActionFunctionArgs) => {
    const form = await request.formData()
    const text = String(form.get(decision === 'accept' ? 'notes' : 'reason') ?? '')
    return write(request, decision, String(form.get('evidenceCode') ?? ''), String(form.get('idempotencyKey') ?? ''), (key, signal) =>
      decideTransfer(
        Number(params.transferId),
        decision,
        decision === 'accept' ? { notes: text || undefined } : { reason: text },
        { idempotencyKey: key, etag: String(form.get('etag') ?? '') },
        signal,
      ),
    )
  }
}

/**
 * Runs a write and turns every ending into a WriteResult. No retries here: a refused write is an answer, and a write
 * with no answer is for the user to retry, with the same Idempotency-Key, once they have seen the current state.
 * Statuses mirror the answer (503 for unknown), so the evidence page knows to read itself again.
 */
async function write(
  request: Request,
  kind: WriteKind,
  code: string,
  key: string,
  send: (key: string, signal: AbortSignal) => Promise<{ status: number; body: TransferResource; headers: Headers }>,
) {
  const page = code ? `/evidence/${encodeURIComponent(code)}` : '/'
  return signedIn(request, async (session) => {
    // Cleared here, not by the page: the answer may land after the user has left it.
    const settled = () => clearIntent(intentScope(kind === 'request' ? 'request' : 'decision', session.user.id, code), key)
    try {
      const sent = await send(key, withTimeout(request.signal, AbortSignal.timeout(WRITE_TIMEOUT_MS)))
      settled()
      return data<WriteResult>({ outcome: 'done', write: kind, transfer: sent.body, replayed: sent.headers.get('Idempotent-Replayed') === 'true' }, { status: sent.status })
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) throw error // signedIn sends the user to sign in again
      if (error instanceof ApiError && error.status === 429)
        return data<WriteResult>({ outcome: 'throttled', write: kind, retryAfterSeconds: error.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS }, { status: 429 })
      if (error instanceof ApiError && !outcomeUnknown(error)) {
        // Nothing was saved and the same key may be sent again.
        if (error.problem?.type !== PROBLEM.concurrentWrite) settled()
        return data<WriteResult>({ outcome: 'refused', write: kind, status: error.status, problem: error.problem }, { status: error.status })
      }
      const answered = error instanceof ApiError ? { status: error.status, problem: error.problem } : { status: 0 }
      return data<WriteResult>({ outcome: 'unknown', write: kind, ...answered }, { status: 503 })
    }
  }, page)
}

/** A server error may come after the write committed, and an in-flight answer means the first send is still running. */
function outcomeUnknown(error: ApiError): boolean {
  return error.status >= 500 || error.problem?.type === PROBLEM.inFlight
}
