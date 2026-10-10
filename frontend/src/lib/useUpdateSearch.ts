import { useCallback } from 'react'
import { useLocation, useNavigate, useNavigation } from 'react-router'
import { useRouterNow } from './useRouterNow'

/** Search string of the pending navigation, else of the current URL. */
export function usePendingSearch(): string {
  const location = useLocation()
  const navigation = useNavigation()
  return (navigation.location ?? location).search
}

/**
 * Edits URL search params on top of any pending navigation, read from the router as it is now, so quick successive
 * filter changes (chip, then search) compose instead of overwriting each other, even before React has rendered the
 * first or when another component made it.
 * The edit lands on the current page, or on `pathname` when given (a search sent from another page, still loading the
 * inbox); any pending navigation must be going there, since the edit builds on its search. `state` goes into the
 * history entry.
 */
export function useUpdateSearch() {
  const navigate = useNavigate()
  const routerNow = useRouterNow()

  return useCallback(
    (edit: (params: URLSearchParams) => void, options?: { replace?: boolean; pathname?: string; state?: unknown }) => {
      const { location, navigation } = routerNow()
      const params = new URLSearchParams((navigation.location ?? location).search)
      edit(params)
      const search = params.size > 0 ? `?${params}` : ''
      void navigate({ pathname: options?.pathname, search }, { replace: options?.replace, state: options?.state })
    },
    [navigate, routerNow],
  )
}
