import type { LoaderFunctionArgs } from 'react-router'
import { getChain, getEvidence, verifyChain, type VerificationReport } from '../../api/evidence'
import { ApiError, type ProblemDetails } from '../../api/client'
import { listPeople } from '../../api/people'
import { signedIn } from '../../auth/guard'

/** Detail, timeline and the custodians a transfer can go to, together; a newer navigation aborts them all. */
export function evidenceLoader({ request, params }: LoaderFunctionArgs) {
  const code = params.id!
  return signedIn(request, async (session) => {
    const [detail, chain, custodians] = await Promise.all([
      getEvidence(code, request.signal),
      getChain(code, request.signal),
      listPeople('Custodio', request.signal),
    ])
    return { detail, chain, custodians, user: session.user }
  })
}

/** What the verify resource route answers: the report, or why there is none. Never thrown, so the page stays. */
export type VerifyResult =
  | { ok: true; report: VerificationReport }
  | { ok: false; status: number; problem?: ProblemDetails }

/** Resource route for fetcher.load: verifies the chain now. */
export function verifyLoader({ request, params }: LoaderFunctionArgs): Promise<VerifyResult> {
  return signedIn(request, async () => {
    try {
      return { ok: true, report: await verifyChain(params.id!, request.signal) }
    } catch (error) {
      if (error instanceof ApiError && error.status !== 401) return { ok: false, status: error.status, problem: error.problem }
      if (error instanceof ApiError) throw error
      return { ok: false, status: 0 } // no answer
    }
  }, `/evidence/${encodeURIComponent(params.id!)}`)
}
