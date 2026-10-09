import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'
import { forgetPeople } from '../api/people'
import { clearSession } from '../auth/session'
import { clearAllIntents } from '../features/transfers/pendingIntent'

// Vitest globals are off, so Testing Library can't auto-register its cleanup. Every test starts signed out.
afterEach(() => {
  cleanup()
  clearSession()
  forgetPeople()
  clearAllIntents()
  sessionStorage.clear()
})
