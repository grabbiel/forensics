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

// Empty means same origin: the Vite dev proxy or the nginx container forwards /api.
const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '')

/** GETs JSON. Never retries; aborts with the router's signal when a newer navigation starts. */
export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, { signal, headers: { Accept: 'application/json' } })
  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as T
}

/** Builds an ApiError, keeping the problem body only when it really is problem+json. */
async function toApiError(response: Response): Promise<ApiError> {
  const isProblem = response.headers.get('Content-Type')?.includes('application/problem+json') ?? false
  const problem = isProblem ? ((await response.json().catch(() => undefined)) as ProblemDetails | undefined) : undefined
  return new ApiError(response.status, problem)
}
