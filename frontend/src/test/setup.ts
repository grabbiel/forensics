import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'
import { clearSession } from '../auth/session'

// Vitest globals are off, so Testing Library can't auto-register its cleanup. Every test starts signed out.
afterEach(() => {
  cleanup()
  clearSession()
  sessionStorage.clear()
})
