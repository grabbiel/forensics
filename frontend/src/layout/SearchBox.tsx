import { MagnifyingGlass } from '@phosphor-icons/react'
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { NavigationType, useLocation, useNavigate, useNavigation, useNavigationType, type Location, type Path } from 'react-router'
import { useRouterNow } from '../lib/useRouterNow'
import { useUpdateSearch } from '../lib/useUpdateSearch'

export const DEBOUNCE_MS = 300

const INBOX = '/'

/** History state on every navigation this box makes, so it can tell its own from a row, a link, or Back. */
const SENT = { sentBySearchBox: true }

/** Whether this box made the navigation to `location`. Back or Forward to an entry it once made is someone else's. */
const sentByBox = (location: Location, action: NavigationType | undefined) =>
  action !== NavigationType.Pop && location.state?.sentBySearchBox === true

/** The search the box shows at a location: the inbox's `q`, nothing on any other page. */
const queryAt = ({ pathname, search }: Path) => (pathname === INBOX && new URLSearchParams(search).get('q')) || ''

/** A page and the search the box shows on it; never ambiguous, since a pathname holds no `?`. */
const shownAt = (pathname: string, q: string) => `${pathname}?q=${q}`

/**
 * Naver-style ringed search over the inbox. On the inbox it writes `q` after a pause (history replace), or at once on
 * Enter or the button (push). On any other page only Enter or the button searches, opening the inbox with that search
 * on its first page: a pause mid-word neither takes the user away from what they are reading nor reloads it. Pauses
 * while that search loads refine it, as pushes so the page left stays one Back away.
 */
export function SearchBox() {
  const location = useLocation()
  const navigationType = useNavigationType()
  const pending = useNavigation().location
  const onInbox = location.pathname === INBOX
  const urlQuery = queryAt(location)
  const shown = shownAt(location.pathname, urlQuery)
  // A search of the inbox on its way, said from any other page; the inbox says so itself.
  const searching = pending && !onInbox ? queryAt(pending) : ''
  const routerNow = useRouterNow()
  const updateSearch = useUpdateSearch()
  const navigate = useNavigate()
  const [value, setValue] = useState(urlQuery)
  const timer = useRef<number>(undefined)
  const written = useRef(shown)
  // From sending a search from another page until a navigation lands: that search, or whatever overtook it.
  const trip = useRef(false)

  // Adopt URL changes this box didn't make (Back/Forward, "Quitar filtros", another page) and drop any pending write.
  // That includes whatever overtook a search sent from another page, even when it shows the same `q`.
  useEffect(() => {
    const ours = sentByBox(location, navigationType)
    const overtaken = trip.current && !ours
    trip.current = false
    if (ours || (shown === written.current && !overtaken)) return
    written.current = shown
    window.clearTimeout(timer.current)
    setValue(urlQuery)
  }, [location, navigationType, shown, urlQuery])

  useEffect(() => () => window.clearTimeout(timer.current), [])

  /**
   * Whether a pause that ends now may search: on the inbox while the box is in step with it, or on the way to a search
   * the box sent. A row, a link or Back the user chose wins, even one React has not rendered yet.
   */
  function mayRefine() {
    const now = routerNow()
    const heading = now.navigation.location ?? now.location
    const action = now.navigation.location ? now.navigation.historyAction : now.historyAction
    if (heading.pathname !== INBOX) return false
    if (sentByBox(heading, action)) return true
    return !trip.current && now.location.pathname === INBOX && shownAt(INBOX, queryAt(now.location)) === written.current
  }

  /** Cancels any pending write, then sets or clears the inbox's `q` unless this box last wrote or adopted the same. */
  function write(q: string, replace: boolean) {
    window.clearTimeout(timer.current)
    const now = routerNow()
    const fromInbox = now.location.pathname === INBOX
    const toInbox = (now.navigation.location ?? now.location).pathname === INBOX
    if (!q && !toInbox) return // nothing to look for, so no reason to leave the page
    if (shownAt(INBOX, q) === written.current) return
    written.current = shownAt(INBOX, q)
    if (!fromInbox) trip.current = true
    if (!toInbox) {
      // Heading anywhere but the inbox: a new search of the whole inbox, as a page Back leaves.
      void navigate({ pathname: INBOX, search: `?${new URLSearchParams({ q })}` }, { state: SENT })
      return
    }
    updateSearch(
      (params) => {
        if (q) params.set('q', q)
        else params.delete('q')
        params.delete('cursor') // a new search starts on the first page
      },
      // Still loading a search sent from another page: replacing would drop that page from history.
      { replace: replace && fromInbox, pathname: INBOX, state: SENT },
    )
  }

  function onChange(event: ChangeEvent<HTMLInputElement>) {
    const next = event.target.value
    setValue(next)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      if (mayRefine()) write(next.trim(), true)
    }, DEBOUNCE_MS)
  }

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    write(value.trim(), false)
  }

  return (
    <form role="search" className="search" onSubmit={onSubmit}>
      <label htmlFor="evidence-search" className="visually-hidden">
        Buscar evidencias por código o descripción
      </label>
      <input
        id="evidence-search"
        className="search__input"
        type="search"
        name="q"
        placeholder="Código (EML2026…) o descripción"
        autoComplete="off"
        spellCheck={false}
        maxLength={100}
        value={value}
        onChange={onChange}
      />
      <button type="submit" className="search__submit" aria-label="Buscar">
        <MagnifyingGlass size={20} weight="bold" aria-hidden="true" />
      </button>
      {/* There from the start on those pages, so the first message is read too. */}
      {!onInbox && (
        <span className="visually-hidden" role="status">
          {searching && `Buscando «${searching}»…`}
        </span>
      )}
    </form>
  )
}
