import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, Outlet } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { describe, expect, it } from 'vitest'
import { usePendingSearch, useUpdateSearch } from './useUpdateSearch'

/**
 * Tabs and the search they show, above a stand-in inbox and an evidence page whose loader waits until released; the
 * inbox's waits too when `holdInbox`.
 */
function renderEditor(initialEntries: string[], holdInbox = false) {
  let release = () => {}
  const released = new Promise<void>((resolve) => (release = resolve))
  function Tabs() {
    const updateSearch = useUpdateSearch()
    const set = (name: string, value: string) => updateSearch((params) => params.set(name, value))
    return (
      <>
        <output>{usePendingSearch()}</output>
        <button type="button" onClick={() => set('type', 'CSV')}>
          CSV
        </button>
        <button
          type="button"
          onClick={() => {
            set('type', 'CSV')
            set('status', 'Valid')
          }}
        >
          CSV válidas
        </button>
      </>
    )
  }
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: (
          <>
            <Tabs />
            <Outlet />
          </>
        ),
        children: [
          { id: 'inbox', index: true, loader: () => (holdInbox ? released.then(() => null) : null) },
          { id: 'evidence', path: 'evidence/:id', loader: () => released.then(() => null) },
        ],
      },
    ],
    { initialEntries, hydrationData: { loaderData: { inbox: null, evidence: null } } },
  )
  render(<RouterProvider router={router} />)
  return { router, release }
}

const url = ({ pathname, search }: { pathname: string; search: string }) => pathname + search

describe('useUpdateSearch', () => {
  it('composes two edits made in the same moment, before React has rendered the first', async () => {
    const { router } = renderEditor(['/?q=fire'])

    await userEvent.click(screen.getByRole('button', { name: 'CSV válidas' }))

    expect(url(router.state.location)).toBe('/?q=fire&type=CSV&status=Valid')
  })

  it('builds on the page on screen, not on another one still loading', async () => {
    const { router, release } = renderEditor(['/?q=fire&status=Valid'])

    act(() => void router.navigate('/evidence/LOG202609110007')) // a row, still loading
    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))
    await act(async () => release())

    expect(url(router.state.location)).toBe('/?q=fire&status=Valid&type=CSV')
  })

  it('shows a change to this page at once, and keeps showing this page while another one loads', async () => {
    const { router, release } = renderEditor(['/?type=LOG'], true)

    await userEvent.click(screen.getByRole('button', { name: 'CSV' }))
    expect(screen.getByRole('status')).toHaveTextContent('?type=CSV')
    act(() => void router.navigate('/evidence/LOG202609110007')) // a row, still loading
    expect(screen.getByRole('status')).toHaveTextContent('?type=LOG')

    await act(async () => release())
  })
})
