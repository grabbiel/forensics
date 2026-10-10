import { MagnifyingGlass } from '@phosphor-icons/react'
import { useEffect, useLayoutEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useLocation, useNavigate, useNavigation, type Path } from 'react-router'
import { useUpdateSearch } from '../lib/useUpdateSearch'

export const DEBOUNCE_MS = 300

const INBOX = '/'

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
  const onInbox = location.pathname === INBOX
  const urlQuery = queryAt(location)
  const shown = shownAt(location.pathname, urlQuery)
  // Where the user is going counts before it loads: a row just clicked, or a search sent from another page.
  const heading = useNavigation().location ?? location
  const toInbox = heading.pathname === INBOX
  const headingTo = shownAt(heading.pathname, queryAt(heading))
  const updateSearch = useUpdateSearch()
  const navigate = useNavigate()
  const [value, setValue] = useState(urlQuery)
  const timer = useRef<number>(undefined)
  const written = useRef(shown)
  // Read when a write runs, which for a pause is after the user may have moved on. It is as of the last render, and
  // React Router renders a navigation in a transition: one started a moment ago may not be here yet.
  const route = useRef({ onInbox, toInbox, headingTo })

  useLayoutEffect(() => {
    route.current = { onInbox, toInbox, headingTo }
  }, [onInbox, toInbox, headingTo])

  // Adopt URL changes this box didn't make (Back/Forward, "Quitar filtros", another page) and drop any pending write.
  useEffect(() => {
    if (shown === written.current) return
    written.current = shown
    window.clearTimeout(timer.current)
    setValue(urlQuery)
  }, [shown, urlQuery])

  useEffect(() => () => window.clearTimeout(timer.current), [])

  /** Cancels any pending write, then sets or clears the inbox's `q` unless this box last wrote or adopted the same. */
  function write(q: string, replace: boolean) {
    window.clearTimeout(timer.current)
    const { onInbox, toInbox } = route.current
    if (!q && !toInbox) return // nothing to look for, so no reason to leave the page
    if (shownAt(INBOX, q) === written.current) return
    written.current = shownAt(INBOX, q)
    if (!toInbox) {
      // Heading anywhere but the inbox: a new search of the whole inbox, as a page Back leaves.
      void navigate({ pathname: INBOX, search: `?${new URLSearchParams({ q })}` })
      return
    }
    updateSearch(
      (params) => {
        if (q) params.set('q', q)
        else params.delete('q')
        params.delete('cursor') // a new search starts on the first page
      },
      // Still loading a search sent from another page: replacing would drop that page from history.
      { replace: replace && onInbox, pathname: INBOX },
    )
  }

  function onChange(event: ChangeEvent<HTMLInputElement>) {
    const next = event.target.value
    setValue(next)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      // Only on the inbox, or on the way to a search this box sent; a link or Back there drops what was typed instead.
      const { onInbox, toInbox, headingTo } = route.current
      if (toInbox && (onInbox || headingTo === written.current)) write(next.trim(), true)
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
    </form>
  )
}
