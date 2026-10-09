import { data, type ActionFunctionArgs } from 'react-router'
import { ApiError, type ProblemDetails } from '../../api/client'
import { decideTransfer, requestTransfer, type Decision, type TransferResource } from '../../api/transfers'
import { signedIn } from '../../auth/guard'

/** How long a write may take before its outcome counts as unknown. */
export const WRITE_TIMEOUT_MS = 15_000

/**
 * What a transfer action answers. Never thrown, so the page stays mounted and can explain the result:
 * - done: the server did it (or had already done it, when it repeats an earlier request with the same key);
 * - refused: the server answered with a problem; a 409 carries the transfer's current state;
 * - unknown: no answer (network failure or timeout); the server may or may not have done it, so retry with the same key.
 */
export type WriteResult =
  | { outcome: 'done'; transfer: TransferResource; replayed: boolean }
  | { outcome: 'refused'; status: number; problem?: ProblemDetails }
  | { outcome: 'unknown' }

/** POST /evidence/:id/transfer: form fields toCustodianId, reason, idempotencyKey. */
export async function requestTransferAction({ request, params }: ActionFunctionArgs) {
  const form = await request.formData()
  return write(request, evidencePage(params.id!), (signal) =>
    requestTransfer(
      { evidenceCode: params.id!, toCustodianId: Number(form.get('toCustodianId')), reason: String(form.get('reason') ?? '') },
      String(form.get('idempotencyKey') ?? ''),
      signal,
    ),
  )
}

/** POST /transfers/:transferId/accept or /reject: form fields evidenceCode, etag, idempotencyKey, and notes or reason. */
export function decisionAction(decision: Decision) {
  return async ({ request, params }: ActionFunctionArgs) => {
    const form = await request.formData()
    const text = String(form.get(decision === 'accept' ? 'notes' : 'reason') ?? '')
    return write(request, evidencePage(String(form.get('evidenceCode') ?? '')), (signal) =>
      decideTransfer(
        Number(params.transferId),
        decision,
        decision === 'accept' ? { notes: text || undefined } : { reason: text },
        { idempotencyKey: String(form.get('idempotencyKey') ?? ''), etag: String(form.get('etag') ?? '') },
        signal,
      ),
    )
  }
}

/** The page a write is made from, to come back to if the user must sign in again. */
const evidencePage = (code: string) => (code ? `/evidence/${encodeURIComponent(code)}` : '/')

/** The router's signal, also aborted after `ms`; a fallback where AbortSignal.any is missing (Safari < 17.4). */
function withTimeout(signal: AbortSignal, ms: number): AbortSignal {
  const timeout = AbortSignal.timeout(ms)
  if (typeof AbortSignal.any === 'function') return AbortSignal.any([signal, timeout])
  const controller = new AbortController()
  const abort = (source: AbortSignal) => () => controller.abort(source.reason)
  signal.addEventListener('abort', abort(signal), { once: true })
  timeout.addEventListener('abort', abort(timeout), { once: true })
  return controller.signal
}

/**
 * Runs a write and turns every ending into a WriteResult. No retries here: a refused write is an answer, and a write
 * with no answer is for the user to retry, with the same Idempotency-Key, once they have seen the current state.
 * Statuses mirror the answer, so the router only revalidates loaders after a success.
 */
async function write(request: Request, page: string, send: (signal: AbortSignal) => Promise<{ status: number; body: TransferResource; headers: Headers }>) {
  return signedIn(request, async () => {
    try {
      const sent = await send(withTimeout(request.signal, WRITE_TIMEOUT_MS))
      const result: WriteResult = { outcome: 'done', transfer: sent.body, replayed: sent.headers.get('Idempotent-Replayed') === 'true' }
      return data(result, { status: sent.status })
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) throw error // signedIn sends the user to sign in again
      if (error instanceof ApiError) return data<WriteResult>({ outcome: 'refused', status: error.status, problem: error.problem }, { status: error.status })
      return data<WriteResult>({ outcome: 'unknown' }, { status: 503 })
    }
  }, page)
}
