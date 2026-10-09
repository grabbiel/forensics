interface ImportMetaEnv {
  /** API origin, e.g. https://app-evidencechain.azurewebsites.net. Empty = same origin (/api proxied). */
  readonly VITE_API_BASE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
