import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll } from 'vitest'

/** The network for MSW tests: requests go through the real fetch and API client, and are answered here. */
export const server = setupServer()

/** Starts MSW for the calling test file. A request no handler answers fails the test instead of reaching a network. */
export function setupMsw(): void {
  beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
  afterEach(() => server.resetHandlers())
  afterAll(() => server.close())
}
