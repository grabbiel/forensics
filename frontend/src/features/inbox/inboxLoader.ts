import type { LoaderFunctionArgs } from 'react-router'
import { isEvidenceType, isIntegrityStatus, listEvidence, type EvidenceFilter } from '../../api/evidence'
import { listPeople } from '../../api/people'
import { signedIn } from '../../auth/guard'

/** Reads the filter from URL params, the single source of truth. Anything unknown is ignored. */
export function readFilter(params: URLSearchParams): EvidenceFilter {
  const type = params.get('type')
  const custodianId = Number(params.get('custodianId'))
  const status = params.get('status')
  return {
    q: params.get('q')?.trim() || undefined,
    type: isEvidenceType(type) ? type : undefined,
    custodianId: Number.isInteger(custodianId) && custodianId > 0 ? custodianId : undefined,
    status: isIntegrityStatus(status) ? status : undefined,
    // Newest first is the default and stays out of the URL.
    sort: params.get('sort') === 'lastEventAt:asc' ? 'lastEventAt:asc' : undefined,
    cursor: params.get('cursor') || undefined,
  }
}

/**
 * Loads one inbox page and the custodians the filter can name. request.signal aborts both when a newer navigation
 * starts, so a stale response never renders.
 */
export function inboxLoader({ request }: LoaderFunctionArgs) {
  const filter = readFilter(new URL(request.url).searchParams)
  return signedIn(request, async () => {
    const [page, custodians] = await Promise.all([listEvidence(filter, request.signal), listPeople('Custodio', request.signal)])
    return { rows: page.items, nextCursor: page.nextCursor, filter, custodians }
  })
}
