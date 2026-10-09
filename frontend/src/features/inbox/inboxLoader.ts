import type { LoaderFunctionArgs } from 'react-router'
import { isEvidenceType, listEvidence, type EvidenceFilter } from '../../api/evidence'
import { signedIn } from '../../auth/guard'

/** Reads the filter from URL params (the single source of truth). Unknown types are ignored. */
export function readFilter(params: URLSearchParams): EvidenceFilter {
  const q = params.get('q')?.trim() || undefined
  const type = params.get('type')
  return { q, type: isEvidenceType(type) ? type : undefined }
}

/** Loads the inbox. request.signal aborts it when a newer navigation starts, so stale responses never render. */
export function inboxLoader({ request }: LoaderFunctionArgs) {
  const filter = readFilter(new URL(request.url).searchParams)
  return signedIn(request, async () => {
    const page = await listEvidence(filter, request.signal)
    return { rows: page.items, nextCursor: page.nextCursor, filter }
  })
}
