import type { RouteObject, ShouldRevalidateFunctionArgs } from 'react-router'
import { guardLoader } from './auth/guard'
import { NotFound } from './features/NotFound'
import { RouteError } from './features/RouteError'
import { LoginPage } from './features/auth/LoginPage'
import { loginAction, loginLoader, logoutAction } from './features/auth/loginRoute'
import { EvidencePage } from './features/evidence/EvidencePage'
import { evidenceLoader, verifyLoader } from './features/evidence/evidenceLoader'
import { InboxPage } from './features/inbox/InboxPage'
import { inboxLoader } from './features/inbox/inboxLoader'
import { decisionAction, requestTransferAction } from './features/transfers/transferActions'
import { AppShell, DetailLoading, PageLoading } from './layout/AppShell'

const EVIDENCE_NOT_FOUND = { title: 'Evidencia no encontrada', detail: 'No existe ninguna evidencia con ese código.' }

/**
 * The router revalidates loaders only after successful actions. A write refused with a 409 or left without an answer
 * (503) also changes what the evidence page should show, so it reads the evidence again then as well.
 */
export function revalidateEvidence({ actionStatus, defaultShouldRevalidate }: ShouldRevalidateFunctionArgs) {
  return actionStatus === 409 || actionStatus === 503 || defaultShouldRevalidate
}

/** Route table, shared by the browser router and the tests' memory router. */
export const routes: RouteObject[] = [
  { path: '/login', loader: loginLoader, action: loginAction, element: <LoginPage />, errorElement: <RouteError />, hydrateFallbackElement: null },
  { path: '/logout', action: logoutAction },

  // Resource routes: no element, only the targets of fetchers. They answer with results, never throw API errors, so a
  // failed verification or write is explained in place instead of replacing the page. A verification records a status
  // on the server, so it runs only when asked: never again because some other write succeeded.
  { path: '/evidence/:id/verify', loader: verifyLoader, shouldRevalidate: () => false },
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
      {
        path: 'evidence/:id',
        loader: evidenceLoader,
        shouldRevalidate: revalidateEvidence,
        element: <EvidencePage />,
        errorElement: <RouteError notFound={EVIDENCE_NOT_FOUND} />,
        hydrateFallbackElement: <DetailLoading />,
      },
      { path: '*', loader: guardLoader, element: <NotFound />, hydrateFallbackElement: null },
    ],
  },
]
