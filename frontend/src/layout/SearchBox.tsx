import { MagnifyingGlass } from '@phosphor-icons/react'
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { useUpdateSearch } from '../lib/useUpdateSearch'

export const DEBOUNCE_MS = 300

/** Naver-style ringed search. Writes `q` after a pause (history replace), or at once on Enter or the button (push). */
export function SearchBox() {
  const [searchParams] = useSearchParams()
  const urlQuery = searchParams.get('q') ?? ''
  const updateSearch = useUpdateSearch()
  const [value, setValue] = useState(urlQuery)
  const timer = useRef<number>(undefined)
  const written = useRef(urlQuery)

  // Adopt URL changes this box didn't make (Back/Forward, "Quitar filtros") and drop any pending write.
  useEffect(() => {
    if (urlQuery === written.current) return
    written.current = urlQuery
    window.clearTimeout(timer.current)
    setValue(urlQuery)
  }, [urlQuery])

  useEffect(() => () => window.clearTimeout(timer.current), [])

  /** Cancels any pending write, then sets or clears `q` unless the URL already has it. */
  function write(q: string, replace: boolean) {
    window.clearTimeout(timer.current)
    if (q === written.current) return
    written.current = q
    updateSearch(
      (params) => {
        if (q) params.set('q', q)
        else params.delete('q')
        params.delete('cursor') // a new search starts on the first page
      },
      { replace },
    )
  }

  function onChange(event: ChangeEvent<HTMLInputElement>) {
    const next = event.target.value
    setValue(next)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => write(next.trim(), true), DEBOUNCE_MS)
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
