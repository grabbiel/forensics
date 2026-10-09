import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/ · https://vitest.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Same-origin /api in dev, like nginx in compose: the API's launch profile listens on 5080;
    // API_PROXY_TARGET=http://localhost:8081 uses the compose API instead.
    proxy: { '/api': process.env.API_PROXY_TARGET ?? 'http://localhost:5080' },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    unstubGlobals: true,
  },
})
