import { useCallback, useLayoutEffect, useRef } from 'react'
import { useLocation, useNavigate, useNavigation } from 'react-router'

/** Search string of the pending navigation, else of the current URL. */
export function usePendingSearch(): string {
  const location = useLocation()
  const navigation = useNavigation()
  return (navigation.location ?? location).search
}

/**
 * Edits URL search params on top of any pending navigation, so quick successive
 * filter changes (chip, then search) compose instead of overwriting each other.
 */
export function useUpdateSearch() {
  const navigate = useNavigate()
  const pendingSearch = usePendingSearch()
  const latest = useRef(pendingSearch)

  useLayoutEffect(() => {
    latest.current = pendingSearch
  }, [pendingSearch])

  return useCallback(
    (edit: (params: URLSearchParams) => void, options?: { replace?: boolean }) => {
      const params = new URLSearchParams(latest.current)
      edit(params)
      const search = params.size > 0 ? `?${params}` : ''
      latest.current = search
      void navigate({ search }, { replace: options?.replace })
    },
    [navigate],
  )
}
