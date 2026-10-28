/**
 * Тонкая обёртка над fetch для API.
 *
 * Все ошибки приходят как ProblemDetails (см. `BarberSearch.Web/Common/FailureProblemDetails.cs`),
 * поэтому клиентские ветки разбирают коды из расширения `errorCodes`, а не текст `detail`.
 */

export type ProblemDetails = {
  type?: string
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
  errorCodes?: string[]
}

export class ApiError extends Error {
  /** Код состояния HTTP; 0 — запрос не дошёл до сервера. */
  readonly status: number

  /** Тело ProblemDetails, если сервер его прислал. */
  readonly problem: ProblemDetails | null

  /** Доменные коды ошибок из расширения `errorCodes`. */
  readonly codes: readonly string[]

  constructor(
    status: number,
    problem: ProblemDetails | null,
    message: string
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.codes = problem?.errorCodes ?? []
  }

  hasCode(code: string): boolean {
    return this.codes.includes(code)
  }
}

const API_PREFIX = '/api'

export async function apiRequest<TResponse>(
  path: string,
  init: RequestInit = {}
): Promise<TResponse> {
  const { headers, ...requestInit } = init

  let response: Response
  try {
    response = await fetch(`${API_PREFIX}${path}`, {
      ...requestInit,
      // Браузерная ветка аутентификации живёт на HttpOnly-cookie.
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        ...(requestInit.body === undefined
          ? {}
          : { 'Content-Type': 'application/json' }),
        ...headers,
      },
    })
  } catch {
    throw new ApiError(0, null, 'Нет связи с сервером.')
  }

  const body = await readJson(response)

  if (!response.ok) {
    throw new ApiError(
      response.status,
      isProblemDetails(body) ? body : null,
      body && isProblemDetails(body) && body.detail
        ? body.detail
        : `HTTP ${response.status}`
    )
  }

  return body as TResponse
}

async function readJson(response: Response): Promise<unknown> {
  const text = await response.text()
  if (!text) {
    return null
  }

  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null
}
