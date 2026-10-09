import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, expect } from 'vitest'

/** The network for MSW tests: requests go through the real fetch and API client, and are answered here. */
export const server = setupServer()

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
