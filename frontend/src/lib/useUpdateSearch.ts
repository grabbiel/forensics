import { useCallback } from 'react'
import { useLocation, useNavigate, useNavigation, type Location } from 'react-router'
import { useRouterNow } from './useRouterNow'

/**
 * History state on "Quitar filtros". It clears the search too, so the header search box drops what was typed and not
 * yet sent, as it does on Back or Forward, even when no search was applied.
 */
export const CLEARS_FILTERS = { clearsFilters: true }

/** Whether `location` is an entry "Quitar filtros" made, or an edit that took its place while it loaded. */
export const clearsFilters = (location: Location) => location.state?.clearsFilters === CLEARS_FILTERS.clearsFilters

/** Search string of the pending navigation when it stays on this page, else of the current URL. */
export function usePendingSearch(): string {
  const location = useLocation()
  const pending = useNavigation().location
  return (pending?.pathname === location.pathname ? pending : location).search
}

/**
 * Edits URL search params on top of any pending navigation to the same page, read from the router as it is now, so
 * quick successive filter changes (chip, then search) compose instead of overwriting each other, even before React has
 * rendered the first or when another component made it. A navigation to another page (a row just clicked) says
 * nothing about this one's search, so the edit builds on the current URL then.
 * The edit lands on the current page, or on `pathname` when given (a search sent from another page, still loading the
 * inbox). `state` goes into the history entry; an edit built on "Quitar filtros" still loading takes its place, so
 * without a `state` of its own it keeps the clear's.
 */
export function useUpdateSearch() {
  const navigate = useNavigate()
  const routerNow = useRouterNow()

  return useCallback(
    (edit: (params: URLSearchParams) => void, options?: { replace?: boolean; pathname?: string; state?: unknown }) => {
      const { location, navigation } = routerNow()
      const pathname = options?.pathname ?? location.pathname
      const base = [navigation.location, location].find((at) => at?.pathname === pathname)
      const params = new URLSearchParams(base?.search)
      edit(params)
      const search = params.size > 0 ? `?${params}` : ''
      const state = options?.state ?? (base && base === navigation.location && clearsFilters(base) ? CLEARS_FILTERS : undefined)
      void navigate({ pathname: options?.pathname, search }, { replace: options?.replace, state })
    },
    [navigate, routerNow],
  )
}
