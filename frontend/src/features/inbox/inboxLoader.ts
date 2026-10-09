import { redirect, type LoaderFunctionArgs } from 'react-router'
import { ApiError } from '../../api/client'
import { isEvidenceType, isIntegrityStatus, listEvidence, type EvidenceFilter } from '../../api/evidence'
import { listPeople, type Person } from '../../api/people'
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
 * starts, so a stale response never renders. A cursor the API refuses (an old link, or one from another filter)
 * sends the user to the first page of the same listing instead of an error that retrying cannot fix.
 */
export function inboxLoader({ request }: LoaderFunctionArgs) {
  const url = new URL(request.url)
  const filter = readFilter(url.searchParams)
  return signedIn(request, async () => {
    // Without the custodian list the filter offers only "Todos"; the inbox itself still loads.
    const custodians = listPeople('Custodio', request.signal).catch((): Person[] => [])
    try {
      const page = await listEvidence(filter, request.signal)
      return { rows: page.items, nextCursor: page.nextCursor, filter, custodians: await custodians }
    } catch (error) {
      if (filter.cursor && error instanceof ApiError && error.status === 400 && error.problem?.errors?.cursor) {
        url.searchParams.delete('cursor')
        throw redirect(`${url.pathname}${url.search}`)
      }
      throw error
    }
  })
}
