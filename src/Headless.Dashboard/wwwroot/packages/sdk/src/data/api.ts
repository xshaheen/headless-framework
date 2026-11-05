/** One call to the dashboard's API, relative to its API base: `diagnostics/commands`, not `/headless/api/...`. */
export interface ApiRequest {
  method: 'GET' | 'POST'
  path: string
  query?: Readonly<Record<string, string | readonly string[]>>
  body?: unknown
}

/** A failed API call: the HTTP status and the problem details the server answered with. */
export class DashboardApiError extends Error {
  readonly status: number
  /** Per-field messages of a `400` validation problem. */
  readonly errors: Readonly<Record<string, readonly string[]>>

  constructor(status: number, message: string, errors: Readonly<Record<string, readonly string[]>> = {}) {
    super(message)
    this.name = 'DashboardApiError'
    this.status = status
    this.errors = errors
  }
}

/** The dashboard's API client. Every call goes through `request`, so tests replace only that. */
export interface DashboardApi {
  request<T>(request: ApiRequest): Promise<T>
}

export interface DashboardApiOptions {
  /** The API base from the page state, an absolute path such as `/headless/api`. */
  apiBase: string
  fetch?: typeof globalThis.fetch
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function fieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) {
    return {}
  }

  const errors: Record<string, string[]> = {}

  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      errors[field] = messages.filter((message): message is string => typeof message === 'string')
    }
  }

  return errors
}

/** Reads the problem details of a failed response into an error whose message the UI can show as is. */
export function toApiError(status: number, body: unknown): DashboardApiError {
  if (typeof body === 'string' && body.trim()) {
    return new DashboardApiError(status, body.trim())
  }

  if (isRecord(body)) {
    const errors = fieldErrors(body.errors)
    const first = Object.values(errors).flat()[0]
    const message =
      (typeof body.detail === 'string' && body.detail) || first || (typeof body.title === 'string' && body.title)

    if (message) {
      return new DashboardApiError(status, message, errors)
    }
  }

  return new DashboardApiError(
    status,
    status === 403 ? 'Needs operate permission' : `The dashboard answered ${status}. Try again.`,
  )
}

/** Builds `path` and `query` into a URL under `apiBase`. */
export function apiUrl(apiBase: string, path: string, query?: ApiRequest['query']) {
  const search = new URLSearchParams()

  for (const [name, value] of Object.entries(query ?? {})) {
    for (const item of typeof value === 'string' ? [value] : value) {
      search.append(name, item)
    }
  }

  const text = search.toString()

  return `${apiBase.replace(/\/$/, '')}/${path.replace(/^\//, '')}${text ? `?${text}` : ''}`
}

/**
 * Creates the API client the shell hands every module. Writes carry `X-Requested-With`, which the server requires
 * of every state change, and the `ui` channel, which decides the commands offered and what the operator log records.
 */
export function createDashboardApi(options: DashboardApiOptions): DashboardApi {
  const fetcher = options.fetch ?? globalThis.fetch.bind(globalThis)

  return {
    async request<T>(request: ApiRequest): Promise<T> {
      const headers: Record<string, string> = { Accept: 'application/json' }

      if (request.method !== 'GET') {
        headers['Content-Type'] = 'application/json'
        headers['X-Requested-With'] = 'XMLHttpRequest'
        headers['X-Headless-Dashboard-Channel'] = 'ui'
      }

      const response = await fetcher(apiUrl(options.apiBase, request.path, request.query), {
        method: request.method,
        headers,
        credentials: 'same-origin',
        body: request.method === 'GET' ? undefined : JSON.stringify(request.body ?? {}),
      })
      const text = await response.text()
      let body: unknown = text

      try {
        body = text ? (JSON.parse(text) as unknown) : undefined
      } catch {
        // A plain-text answer stays text, such as the integrity filter's 400.
      }

      if (!response.ok) {
        throw toApiError(response.status, body)
      }

      return body as T
    },
  }
}
