import type { RouteObject } from 'react-router'
import { NotFound } from './features/NotFound'
import { RouteError } from './features/RouteError'
import { InboxPage } from './features/inbox/InboxPage'
import { inboxLoader } from './features/inbox/inboxLoader'
import { AppShell, PageLoading } from './layout/AppShell'

/** Route table, shared by the browser router and the tests' memory router. */
export const routes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteError />,
    children: [
      // The fallback sits on the index route so the header renders while the first fetch runs.
      { index: true, loader: inboxLoader, element: <InboxPage />, errorElement: <RouteError />, hydrateFallbackElement: <PageLoading /> },
      { path: '*', element: <NotFound /> },
    ],
  },
]
