import { act, fireEvent, render, screen } from '@testing-library/react'
import { createMemoryRouter, Outlet, type InitialEntry, type LoaderFunctionArgs } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DEBOUNCE_MS, SearchBox } from './SearchBox'

const code = 'LOG202609110007'

/**
 * Mounts the search box above stand-in inbox and evidence pages, as the app shell does. Their loaders only record the
 * URLs they load after the first render, so no fetch is involved; `hold` keeps one page loading until released.
 */
function renderSearch(initialEntries: InitialEntry[] = ['/'], initialIndex?: number) {
  const loads: string[] = []
  const waits: Partial<Record<'inbox' | 'evidence', Promise<void>>> = {}
  const record = (page: 'inbox' | 'evidence') => async ({ request }: LoaderFunctionArgs) => {
    const { pathname, search } = new URL(request.url)
    loads.push(pathname + search)
    await waits[page]
    return null
  }
  const router = createMemoryRouter(
    [
      {
        path: '/',
        element: (
          <>
            <SearchBox />
            <Outlet />
          </>
        ),
        children: [
          { id: 'inbox', index: true, loader: record('inbox') },
          { id: 'evidence', path: 'evidence/:id', loader: record('evidence') },
        ],
      },
    ],
    { initialEntries, initialIndex, hydrationData: { loaderData: { inbox: null, evidence: null } } },
  )
  render(<RouterProvider router={router} />)
  function hold(page: 'inbox' | 'evidence') {
    let release = () => {}
    waits[page] = new Promise((resolve) => (release = resolve))
    return release
  }
  return { router, loads, hold, input: screen.getByRole('searchbox') }
}

const advance = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms))
const typeInto = (input: HTMLElement, value: string) => fireEvent.change(input, { target: { value } })
const submit = (input: HTMLElement) => act(async () => fireEvent.submit(input.closest('form')!))
const url = ({ pathname, search }: { pathname: string; search: string }) => pathname + search

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
    await submit(input)
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

  it('starts the listing again from the first page', async () => {
    const { router, input } = renderSearch(['/?type=LOG&cursor=abc'])

    typeInto(input, 'vpn')
    await advance(DEBOUNCE_MS)

    expect(router.state.location.search).toBe('?type=LOG&q=vpn')
  })

  it('drops q when the field is cleared and submitted', async () => {
    const { router, input } = renderSearch(['/?q=old'])

    typeInto(input, '')
    await submit(input)

    expect(router.state.location.search).toBe('')
  })

  it('lets a page opened while the user typed load, instead of searching over it', async () => {
    const { router, loads, hold, input } = renderSearch(['/?q=vpn'])
    const release = hold('evidence')

    typeInto(input, 'vpn fw')
    act(() => void router.navigate(`/evidence/${code}`)) // a row clicked before the pause ends
    await advance(DEBOUNCE_MS)
    await act(async () => release())

    expect(url(router.state.location)).toBe(`/evidence/${code}`)
    expect(loads).toEqual([`/evidence/${code}`])
    expect(input).toHaveValue('')
  })

  it('lets a row clicked just as the pause ends load too, before React has rendered the click', async () => {
    const { router, loads, hold, input } = renderSearch(['/?q=vpn'])
    const release = hold('evidence')

    typeInto(input, 'vpn fw')
    await advance(DEBOUNCE_MS - 1)
    await act(async () => {
      void router.navigate(`/evidence/${code}`) // React renders this in a transition, once the act is over
      await vi.advanceTimersByTimeAsync(1)
    })
    await act(async () => release())

    expect(url(router.state.location)).toBe(`/evidence/${code}`)
    expect(loads).toEqual([`/evidence/${code}`])
    expect(input).toHaveValue('')
  })

  it('builds on a filter clicked just as the pause ends, before React has rendered the click', async () => {
    const { router, loads, hold, input } = renderSearch(['/?type=LOG'])
    const release = hold('inbox')

    typeInto(input, 'vpn')
    await advance(DEBOUNCE_MS - 1)
    await act(async () => {
      void router.navigate('/?type=CSV') // the CSV tab; React renders it in a transition, once the act is over
      await vi.advanceTimersByTimeAsync(1)
    })
    await act(async () => release())

    expect(url(router.state.location)).toBe('/?type=CSV&q=vpn')
    expect(loads).toEqual(['/?type=CSV', '/?type=CSV&q=vpn'])
    await act(() => router.navigate(-1)) // the tab's new entry is kept, so Back undoes both
    expect(url(router.state.location)).toBe('/?type=LOG')
  })

  it('keeps the page before an Enter when a pause refines it before it loads', async () => {
    const { router, hold, input } = renderSearch(['/?type=LOG'])
    const release = hold('inbox')

    typeInto(input, 'b')
    await submit(input)
    typeInto(input, 'bc')
    await advance(DEBOUNCE_MS)
    await act(async () => release())

    expect(url(router.state.location)).toBe('/?type=LOG&q=bc')
    await act(() => router.navigate(-1))
    expect(url(router.state.location)).toBe('/?type=LOG')
  })

  it('lets a slow Back win over a pause that ends while it loads, even to the same search', async () => {
    const { router, loads, hold, input } = renderSearch(['/?q=fire', '/?q=fire&type=LOG'], 1)
    const release = hold('inbox')

    typeInto(input, 'fire edge')
    act(() => void router.navigate(-1))
    await advance(DEBOUNCE_MS)
    await act(async () => release())

    expect(url(router.state.location)).toBe('/?q=fire')
    expect(loads).toEqual(['/?q=fire'])
    expect(input).toHaveValue('fire')
  })

  it('keeps refining when a filter clicked while its last search loads carries that search along', async () => {
    const { router, loads, hold, input } = renderSearch(['/?q=fire'])
    const release = hold('inbox')

    typeInto(input, 'fire ed')
    await advance(DEBOUNCE_MS)
    act(() => void router.navigate('/?q=fire+ed&type=LOG')) // the LOG tab, built on the search still loading
    typeInto(input, 'fire edge')
    await advance(DEBOUNCE_MS)
    await act(async () => release())

    expect(url(router.state.location)).toBe('/?q=fire+edge&type=LOG')
    expect(loads).toEqual(['/?q=fire+ed', '/?q=fire+ed&type=LOG', '/?q=fire+edge&type=LOG'])
    expect(input).toHaveValue('fire edge')
  })

  it('lets a Back that lands just as the pause ends win, before React has rendered it', async () => {
    const { router, loads, hold, input } = renderSearch(['/?q=fire', '/?q=fire&type=LOG'], 1)
    const release = hold('inbox')

    typeInto(input, 'fire edge')
    await advance(DEBOUNCE_MS - 1)
    await act(async () => {
      void router.navigate(-1)
      release() // Back lands; React renders it in a transition, once the act is over
      await vi.advanceTimersByTimeAsync(1)
    })

    expect(url(router.state.location)).toBe('/?q=fire')
    expect(loads).toEqual(['/?q=fire'])
    expect(input).toHaveValue('fire')
  })

  it('drops a pending write when Back lands, even on the same search', async () => {
    const { router, input } = renderSearch(['/?q=b', '/?q=b&type=LOG'], 1)

    typeInto(input, 'bc')
    await act(() => router.navigate(-1))
    await advance(DEBOUNCE_MS * 2)

    expect(url(router.state.location)).toBe('/?q=b')
    expect(input).toHaveValue('b')
  })

  describe('on any other page', () => {
    it('searches nothing and reloads nothing while the user types', async () => {
      const { router, loads, input } = renderSearch([`/evidence/${code}`])

      typeInto(input, 'firewall')
      await advance(DEBOUNCE_MS * 2)

      expect(url(router.state.location)).toBe(`/evidence/${code}`)
      expect(loads).toEqual([])
      expect(input).toHaveValue('firewall')
    })

    it('opens the inbox on Enter with only that search, as a new page Back returns from', async () => {
      // The page's own URL may carry a q and cursor that an older box wrote there; they say nothing about the inbox.
      const { router, loads, input } = renderSearch(['/?type=LOG', `/evidence/${code}?q=old&cursor=abc`], 1)
      expect(input).toHaveValue('')

      typeInto(input, 'firewall')
      await submit(input)

      expect(url(router.state.location)).toBe('/?q=firewall')
      expect(router.state.historyAction).toBe('PUSH')
      expect(loads).toEqual(['/?q=firewall'])
      expect(input).toHaveValue('firewall')

      await act(() => router.navigate(-1))
      expect(url(router.state.location)).toBe(`/evidence/${code}?q=old&cursor=abc`)
      expect(input).toHaveValue('')
      await act(() => router.navigate(1)) // Forward to the entry the box made is not the box's doing
      expect(input).toHaveValue('firewall')
    })

    it('keeps searching what the user types while the inbox loads, and keeps the page they left', async () => {
      const { router, loads, hold, input } = renderSearch([`/evidence/${code}`])
      const release = hold('inbox')

      typeInto(input, 'firewall')
      await submit(input)
      typeInto(input, 'firewall edge')
      await advance(DEBOUNCE_MS)
      typeInto(input, 'firewall edge 01')
      await advance(DEBOUNCE_MS)
      await act(async () => release())

      expect(url(router.state.location)).toBe('/?q=firewall+edge+01')
      expect(loads).toEqual(['/?q=firewall', '/?q=firewall+edge', '/?q=firewall+edge+01'])
      expect(input).toHaveValue('firewall edge 01')

      await act(() => router.navigate(-1))
      expect(url(router.state.location)).toBe(`/evidence/${code}`)
    })

    it('searches as usual on the inbox it opened, after other changes there', async () => {
      const { router, input } = renderSearch([`/evidence/${code}`])

      typeInto(input, 'fire')
      await submit(input)
      await act(() => router.navigate('/?q=fire&type=LOG')) // the LOG tab
      typeInto(input, 'fire edge')
      await advance(DEBOUNCE_MS)

      expect(url(router.state.location)).toBe('/?q=fire+edge&type=LOG')
      expect(router.state.historyAction).toBe('REPLACE')
    })

    it('refines in place a search that landed just as the pause ended, before React has rendered it', async () => {
      const { router, loads, hold, input } = renderSearch([`/evidence/${code}`])
      const releaseSearch = hold('inbox')

      typeInto(input, 'firewall')
      await submit(input)
      typeInto(input, 'firewall edge')
      await advance(DEBOUNCE_MS - 1)
      let releaseRefinement = () => {}
      await act(async () => {
        releaseSearch() // the search lands; React renders it in a transition, once the act is over
        releaseRefinement = hold('inbox')
        await vi.advanceTimersByTimeAsync(1)
      })

      // React shows the search that landed while the refinement loads, and keeps what was typed.
      expect(url(router.state.location)).toBe('/?q=firewall')
      expect(input).toHaveValue('firewall edge')
      await act(async () => releaseRefinement())
      expect(url(router.state.location)).toBe('/?q=firewall+edge')
      expect(loads).toEqual(['/?q=firewall', '/?q=firewall+edge'])
      expect(input).toHaveValue('firewall edge')

      await act(() => router.navigate(-1)) // replaced, so the evidence page is the entry before
      expect(url(router.state.location)).toBe(`/evidence/${code}`)
    })

    it('keeps refining after a tab on the inbox it opened, though it refined before React caught up', async () => {
      const { router, hold, input } = renderSearch([`/evidence/${code}`])
      const releaseSearch = hold('inbox')

      typeInto(input, 'firewall')
      await submit(input)
      typeInto(input, 'firewall edge')
      await advance(DEBOUNCE_MS - 1)
      let releaseRest = () => {}
      await act(async () => {
        releaseSearch() // the search lands; React renders it in a transition, once the act is over
        releaseRest = hold('inbox')
        await vi.advanceTimersByTimeAsync(1)
      })
      act(() => void router.navigate('/?q=firewall+edge&type=LOG')) // the LOG tab, built on the refinement loading
      typeInto(input, 'firewall edge 01')
      await advance(DEBOUNCE_MS)
      await act(async () => releaseRest())

      expect(url(router.state.location)).toBe('/?q=firewall+edge+01&type=LOG')
      expect(input).toHaveValue('firewall edge 01')
    })

    it('says what it is looking for while that search loads, and leaves it to the inbox once there', async () => {
      const { router, hold, input } = renderSearch([`/evidence/${code}`])
      const release = hold('inbox')
      const status = screen.getByRole('status')
      expect(status).toBeEmptyDOMElement()

      typeInto(input, 'firewall')
      await submit(input)
      expect(status).toHaveTextContent('Buscando «firewall»…')

      await act(async () => release())
      expect(url(router.state.location)).toBe('/?q=firewall')
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    it('keeps a search sent while React was still catching up with a page the user had opened', async () => {
      const { router, hold, input } = renderSearch([`/evidence/${code}`])
      const release = hold('inbox')

      typeInto(input, 'firewall')
      await act(async () => {
        await router.navigate('/evidence/CSV202610050001') // a link, loaded; React renders it once the act is over
        fireEvent.submit(input.closest('form')!)
      })
      expect(screen.getByRole('status')).toHaveTextContent('Buscando «firewall»…')
      expect(input).toHaveValue('firewall') // React showing that page is no reason to drop the search
      await act(async () => release())

      expect(url(router.state.location)).toBe('/?q=firewall')
      expect(input).toHaveValue('firewall')
    })

    it('starts a new search on Enter while a Back to a filtered inbox loads, and ignores an empty one', async () => {
      const { router, loads, hold, input } = renderSearch(['/?type=LOG', `/evidence/${code}`], 1)
      const release = hold('inbox')
      act(() => void router.navigate(-1))

      await submit(input)
      expect(router.state.navigation.location && url(router.state.navigation.location)).toBe('/?type=LOG')
      typeInto(input, 'vpn')
      await submit(input)
      await act(async () => release())

      expect(url(router.state.location)).toBe('/?q=vpn')
      expect(loads).toEqual(['/?type=LOG', '/?q=vpn'])
    })

    it('tells a Back from React catching up, though the browser keys both pages "default"', async () => {
      // A fresh load, then the skip link's #main: the browser gives both entries the key "default".
      const entries = [
        { pathname: `/evidence/${code}`, key: 'default' },
        { pathname: `/evidence/${code}`, hash: '#main', key: 'default' },
      ]
      const { router, hold, input } = renderSearch(entries, 1)
      hold('inbox')

      typeInto(input, 'fire')
      await submit(input)
      await act(() => router.navigate(-1)) // Back, while the search loads

      expect(url(router.state.location)).toBe(`/evidence/${code}`)
      expect(input).toHaveValue('')
      typeInto(input, 'fire')
      await submit(input)
      expect(router.state.navigation.location && url(router.state.navigation.location)).toBe('/?q=fire')
    })

    it('stays put on Enter with nothing typed', async () => {
      const { router, loads, input } = renderSearch([`/evidence/${code}`])

      typeInto(input, '  ')
      await submit(input)

      expect(url(router.state.location)).toBe(`/evidence/${code}`)
      expect(loads).toEqual([])
    })

    it('drops what was typed but never sent once the user goes to another page', async () => {
      const { router, loads, input } = renderSearch([`/evidence/${code}`])

      typeInto(input, 'fire')
      await advance(DEBOUNCE_MS)
      await act(() => router.navigate('/')) // the brand link

      expect(url(router.state.location)).toBe('/')
      expect(loads).toEqual(['/'])
      expect(input).toHaveValue('')
    })

    it('drops it too when the user goes back to the inbox before the pause ends, however long the inbox takes', async () => {
      const { router, loads, hold, input } = renderSearch(['/?type=LOG', `/evidence/${code}`], 1)
      const release = hold('inbox')

      typeInto(input, 'fire')
      act(() => void router.navigate(-1))
      await advance(DEBOUNCE_MS)
      await act(async () => release())

      expect(url(router.state.location)).toBe('/?type=LOG')
      expect(loads).toEqual(['/?type=LOG'])
      expect(input).toHaveValue('')

      await act(() => router.navigate(1)) // the evidence page is still one Forward away
      expect(url(router.state.location)).toBe(`/evidence/${code}`)
    })

    it('tells Back from its own search while that loads, even when Back goes to an inbox search like it', async () => {
      // The entry Back goes to is a search the box made earlier.
      const earlier = { pathname: '/', search: '?q=fire&type=LOG', state: { sentBySearchBox: true } }
      const { router, loads, hold, input } = renderSearch([earlier, `/evidence/${code}`], 1)
      const release = hold('inbox')

      typeInto(input, 'fire')
      await submit(input)
      typeInto(input, 'fire edge')
      act(() => void router.navigate(-1))
      await advance(DEBOUNCE_MS)
      await act(async () => release())

      expect(url(router.state.location)).toBe('/?q=fire&type=LOG')
      expect(loads).toEqual(['/?q=fire', '/?q=fire&type=LOG'])
      expect(input).toHaveValue('fire')

      await act(() => router.navigate(1))
      expect(url(router.state.location)).toBe(`/evidence/${code}`)
    })

    it('tells it too when that Back lands just as the pause ends, before React has rendered it', async () => {
      const earlier = { pathname: '/', search: '?q=fire&type=LOG', state: { sentBySearchBox: true } }
      const { router, loads, hold, input } = renderSearch([earlier, `/evidence/${code}`], 1)
      const release = hold('inbox')

      typeInto(input, 'fire')
      await submit(input)
      typeInto(input, 'fire edge')
      await advance(DEBOUNCE_MS - 1)
      await act(async () => {
        void router.navigate(-1)
        release() // Back lands; React renders it in a transition, once the act is over
        await vi.advanceTimersByTimeAsync(1)
      })

      expect(url(router.state.location)).toBe('/?q=fire&type=LOG')
      expect(loads).toEqual(['/?q=fire', '/?q=fire&type=LOG'])
      expect(input).toHaveValue('fire')
    })
  })
})
