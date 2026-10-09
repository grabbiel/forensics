# Evidence Chain · web

React 19 + TypeScript SPA (Vite 8, React Router 8 data mode). Node 24 (`nvm use`).

```bash
npm ci
npm run dev      # http://localhost:5173, proxies /api to http://localhost:5080
npm test         # Vitest + Testing Library
npm run build    # type-check and bundle to dist/
```

`VITE_API_BASE_URL` (see `.env.example`) points the SPA at a remote API; empty means same origin.
The compose stack serves the build from nginx on http://localhost:8080 and proxies `/api`.
