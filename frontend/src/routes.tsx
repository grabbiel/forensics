import type { RouteObject } from 'react-router'
import { NotFound } from './features/NotFound'
import { RouteError } from './features/RouteError'
import { LoginPage } from './features/auth/LoginPage'
import { loginAction, loginLoader, logoutAction } from './features/auth/loginRoute'
import { EvidencePage } from './features/evidence/EvidencePage'
import { evidenceLoader, verifyLoader } from './features/evidence/evidenceLoader'
import { InboxPage } from './features/inbox/InboxPage'
import { inboxLoader } from './features/inbox/inboxLoader'
import { decisionAction, requestTransferAction } from './features/transfers/transferActions'
import { AppShell, PageLoading } from './layout/AppShell'

/** Route table, shared by the browser router and the tests' memory router. */
export const routes: RouteObject[] = [
  { path: '/login', loader: loginLoader, action: loginAction, element: <LoginPage />, errorElement: <RouteError /> },
  { path: '/logout', action: logoutAction },

  // Resource routes: no element, only the targets of fetchers. They answer with results, never throw API errors,
  // so a failed verification or write is explained in place instead of replacing the page.
  { path: '/evidence/:id/verify', loader: verifyLoader },
  { path: '/evidence/:id/transfer', action: requestTransferAction },
  { path: '/transfers/:transferId/accept', action: decisionAction('accept') },
  { path: '/transfers/:transferId/reject', action: decisionAction('reject') },

  {
    path: '/',
    element: <AppShell />,
    errorElement: <RouteError />,
    children: [
      // Fallbacks sit on the pages so the header renders while the first fetch runs.
      { index: true, loader: inboxLoader, element: <InboxPage />, errorElement: <RouteError />, hydrateFallbackElement: <PageLoading /> },
      { path: 'evidence/:id', loader: evidenceLoader, element: <EvidencePage />, errorElement: <RouteError />, hydrateFallbackElement: <PageLoading /> },
      { path: '*', element: <NotFound /> },
    ],
  },
]
