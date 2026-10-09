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

/** What every throttled answer asks of the user, in words a screen reader says well. */
export function waitSentence(seconds: number): string {
  return `Espera ${seconds} ${seconds === 1 ? 'segundo' : 'segundos'} antes de volver a intentarlo.`
}

/** Said politely once the wait is over; the alert that asked for it stays as it was. */
export const READY_SENTENCE = 'Ya puedes volver a intentarlo.'
