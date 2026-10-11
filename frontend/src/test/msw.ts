import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect } from 'vitest'

/**
 * Answers the header's unread poll for every MSW test, so a test that renders the shell needs no handler of its own.
 * Only this GET: listing and marking stay unanswered, so a notifications test that forgets its handler still fails.
 * server.resetHandlers() restores it after each test's server.use overrides.
 */
const defaultHandlers = [http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ unreadCount: 0 }))]

/** The network for MSW tests: requests go through the real fetch and API client, and are answered here. */
export const server = setupServer(...defaultHandlers)

const unhandled: string[] = []

/**
 * Starts MSW for the calling test file. A request no handler answers fails as a network error, which the page may
 * absorb (the custodian list falls back to "Todos"), so the test that made it fails too.
 */
export function setupMsw(): void {
  beforeAll(() =>
    server.listen({
      onUnhandledRequest: (request, print) => {
        unhandled.push(`${request.method} ${new URL(request.url).pathname}`)
        print.error()
      },
    }),
  )
  afterEach(() => {
    server.resetHandlers()
    expect(unhandled.splice(0), 'requests no MSW handler answered').toEqual([])
  })
  afterAll(() => server.close())
}
