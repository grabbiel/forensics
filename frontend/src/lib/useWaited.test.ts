import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useWaited, waitSentence } from './useWaited'

afterEach(() => vi.useRealTimers())

describe('useWaited', () => {
  it('waits the seconds an answer asks for, starts again for a newer answer, and never waits without one', () => {
    vi.useFakeTimers()
    const first = { status: 429 }
    const second = { status: 429 }
    const { result, rerender } = renderHook(({ seconds, answer }) => useWaited(seconds, answer), {
      initialProps: { seconds: 2 as number | undefined, answer: first as unknown },
    })

    expect(result.current).toBe(false)
    act(() => vi.advanceTimersByTime(1_999))
    expect(result.current).toBe(false)
    act(() => vi.advanceTimersByTime(1))
    expect(result.current).toBe(true)

    rerender({ seconds: 1, answer: second })
    expect(result.current).toBe(false)
    act(() => vi.advanceTimersByTime(1_000))
    expect(result.current).toBe(true)

    rerender({ seconds: undefined, answer: undefined })
    expect(result.current).toBe(true)
  })

  it('leaves no timer behind when the page goes during a wait', () => {
    vi.useFakeTimers()
    const { unmount } = renderHook(() => useWaited(30, {}))
    expect(vi.getTimerCount()).toBe(1)

    unmount()

    expect(vi.getTimerCount()).toBe(0)
  })

  it('says the wait in whole words, one second or several', () => {
    expect(waitSentence(1)).toBe('Espera 1 segundo antes de volver a intentarlo.')
    expect(waitSentence(30)).toBe('Espera 30 segundos antes de volver a intentarlo.')
  })
})
