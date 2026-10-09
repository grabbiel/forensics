import { getSession } from '../auth/session'

/** RFC 9457 problem details, as returned by the API for every 4xx/5xx. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
  [extension: string]: unknown
}

/** A non-2xx response, carrying the parsed problem body when there is one. */
export class ApiError extends Error {
  readonly status: number
  readonly problem?: ProblemDetails

  constructor(status: number, problem?: ProblemDetails) {
    super(problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

/** A successful write: its status, body and the headers that matter (ETag, Location, Idempotent-Replayed). */
export interface Sent<T> {
  status: number
  body: T
  headers: Headers
}

// Empty means same origin: the Vite dev proxy or the nginx container forwards /api.
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '')

/** How long a read may take before it counts as no answer, so a hung connection does not leave a page loading. */
export const READ_TIMEOUT_MS = 15_000

/**
 * GETs JSON with the signed-in user's token. Never retries: a newer navigation aborts it through `signal`, and
 * every failure surfaces to the caller; after READ_TIMEOUT_MS it fails as a TimeoutError.
 */
export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const timeout = AbortSignal.timeout(READ_TIMEOUT_MS)
  const response = await fetch(`${baseUrl}${path}`, { signal: signal ? withTimeout(signal, timeout) : timeout, headers: headers({ Accept: 'application/json' }) })
  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as T
}

/**
 * POSTs JSON. Never retries, above all not a 409: a conflict is an answer about the current state, and repeating the
 * write would act on a version the user never saw. Retrying is the user's call, with the same Idempotency-Key.
 */
export async function postJson<T>(path: string, body: unknown, options: { signal?: AbortSignal; headers?: Record<string, string> } = {}): Promise<Sent<T>> {
  const response = await fetch(`${baseUrl}${path}`, {
    method: 'POST',
    signal: options.signal,
    headers: headers({ Accept: 'application/json', 'Content-Type': 'application/json', ...options.headers }),
    body: JSON.stringify(body ?? {}),
  })
  if (!response.ok) throw await toApiError(response)
  return { status: response.status, body: (await response.json()) as T, headers: response.headers }
}

/** `signal`, also aborted when `timeout` fires; a fallback where AbortSignal.any is missing (Safari < 17.4). */
export function withTimeout(signal: AbortSignal, timeout: AbortSignal): AbortSignal {
  if (typeof AbortSignal.any === 'function') return AbortSignal.any([signal, timeout])
  const controller = new AbortController()
  const abort = (source: AbortSignal) => () => controller.abort(source.reason)
  signal.addEventListener('abort', abort(signal), { once: true })
  timeout.addEventListener('abort', abort(timeout), { once: true })
  return controller.signal
}

/** Adds the bearer token when someone is signed in. */
function headers(base: Record<string, string>): Record<string, string> {
  const token = getSession()?.accessToken
  return token ? { ...base, Authorization: `Bearer ${token}` } : base
}

/** Builds an ApiError, keeping the problem body only when it really is problem+json. */
async function toApiError(response: Response): Promise<ApiError> {
  const isProblem = response.headers.get('Content-Type')?.includes('application/problem+json') ?? false
  const problem = isProblem ? ((await response.json().catch(() => undefined)) as ProblemDetails | undefined) : undefined
  return new ApiError(response.status, problem)
}
