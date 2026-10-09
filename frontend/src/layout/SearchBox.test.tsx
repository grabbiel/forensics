import { act, fireEvent, render, screen } from '@testing-library/react'
import { createMemoryRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DEBOUNCE_MS, SearchBox } from './SearchBox'

/** Mounts the search box alone, so no loader or fetch is involved. */
function renderSearch(initialEntries = ['/'], initialIndex?: number) {
  const router = createMemoryRouter([{ path: '/', element: <SearchBox /> }], { initialEntries, initialIndex })
  render(<RouterProvider router={router} />)
  return { router, input: screen.getByRole('searchbox') }
}

const advance = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms))
const typeInto = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } })

// Timer-level tests use fireEvent: user-event's async wrapper waits on a setTimeout that only Jest auto-advances.
describe('SearchBox', () => {
  // Fake only the debounce's timers; React's scheduler keeps its real ones.
  beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }))
  afterEach(() => vi.useRealTimers())

  it('writes q after exactly the debounce, replacing history and keeping other filters', async () => {
    const { router, input } = renderSearch(['/?type=LOG'])

    typeInto(input, 'firewall')
    await advance(DEBOUNCE_MS - 1)
    expect(router.state.location.search).toBe('?type=LOG')

    await advance(1)
    expect(router.state.location.search).toBe('?type=LOG&q=firewall')
    expect(router.state.historyAction).toBe('REPLACE')
  })

  it('applies at once on submit (push) and cancels the pending debounce', async () => {
    const { router, input } = renderSearch()

    typeInto(input, 'vpn')
    await act(async () => fireEvent.submit(input.closest('form')!))
    expect(router.state.location.search).toBe('?q=vpn')
    expect(router.state.historyAction).toBe('PUSH')

    await advance(DEBOUNCE_MS * 2)
    expect(router.state.historyAction).toBe('PUSH')
  })

  it('follows Back even while the input has focus and a write is pending', async () => {
    const { router, input } = renderSearch(['/?q=a', '/?q=b'], 1)
    input.focus()
    typeInto(input, 'bc')

    await act(() => router.navigate(-1))
    await advance(DEBOUNCE_MS * 2)

    expect(input).toHaveValue('a')
    expect(router.state.location.search).toBe('?q=a')
  })

  it('drops q when the field is cleared and submitted', async () => {
    const { router, input } = renderSearch(['/?q=old'])

    typeInto(input, '')
    await act(async () => fireEvent.submit(input.closest('form')!))

    expect(router.state.location.search).toBe('')
  })
})
