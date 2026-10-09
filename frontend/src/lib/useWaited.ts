import { useEffect, useState } from 'react'

/**
 * Whether the wait an answer asked for is over: false for `seconds` after `answer` arrives, true when it asks for none.
 * Keyed by the answer, so a new 429 starts its own wait and the timer of an older one never ends it.
 */
export function useWaited(seconds: number | undefined, answer: unknown): boolean {
  const [over, setOver] = useState<unknown>()
  useEffect(() => {
    if (seconds === undefined) return
    const timer = setTimeout(() => setOver(answer), seconds * 1000)
    return () => clearTimeout(timer)
  }, [seconds, answer])
  return seconds === undefined || over === answer
}
