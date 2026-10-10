import { useCallback, useContext } from 'react'
import { UNSAFE_DataRouterContext, type RouterState } from 'react-router'

/**
 * Reads the router's state at the moment of the call. React Router renders each navigation in a transition, so
 * useLocation and useNavigation report a click a moment after it starts; a timer that fires in between reads this
 * instead. The router sets a GET navigation's state as it starts, before any loader runs, as long as it has no
 * getContext (which it would await first).
 *
 * React Router exports the context that holds the router only as UNSAFE_; SearchBox.test.tsx pins what this needs.
 */
export function useRouterNow(): () => RouterState {
  const router = useContext(UNSAFE_DataRouterContext)?.router
  if (!router) throw new Error('useRouterNow needs a data router (RouterProvider).')
  return useCallback(() => router.state, [router])
}
