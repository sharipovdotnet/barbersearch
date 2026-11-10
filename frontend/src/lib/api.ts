/**
 * Тонкая обёртка над fetch для API.
 *
 * Все ошибки приходят как ProblemDetails (см. `BarberSearch.Web/Common/FailureProblemDetails.cs`),
 * поэтому клиентские ветки разбирают коды из расширения `errorCodes`, а не текст `detail`.
 */

import { t } from '@/i18n'

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

/** Итог попытки продлить сессию: повторять запрос или отдать ошибку наверх. */
export type UnauthorizedResult =
  | { retry: true }
  | { retry: false; error?: ApiError }

export type ApiRequestOptions<TResponse> = {
  /** `false` отключает прозрачное продление сессии на `401` (сам refresh, вход, выход). */
  retryOnUnauthorized?: boolean
  /**
   * Проверка успешного тела: `false` → `ApiError` вместо `body as TResponse`.
   * Не задана — поведение прежнее, тело принимается как есть.
   */
  validate?: (body: unknown) => body is TResponse
}

const API_PREFIX = '/api'

let unauthorizedHandler: (() => Promise<UnauthorizedResult>) | null = null

/**
 * Мост между транспортом и сессией.
 *
 * `api.ts` намеренно не знает про `auth.ts`, иначе получился бы цикл импортов
 * (`auth` → `api` → `auth`). Обработчик регистрирует `SessionProvider`.
 */
export function setUnauthorizedHandler(
  handler: (() => Promise<UnauthorizedResult>) | null
): void {
  unauthorizedHandler = handler
}

/**
 * Запрос к API с одним прозрачным повтором после `401`.
 *
 * Повтор безопасен и для мутаций: `401` формирует middleware аутентификации **до** действия,
 * поэтому запрос не был выполнен. Тело можно отправить повторно, если это строка или
 * `FormData` (её сериализация не расходует объект). Потоковые тела (`ReadableStream`,
 * `Blob`) не повторяем. Второй `401` уже не продлевает сессию.
 */
export async function apiRequest<TResponse>(
  path: string,
  init: RequestInit = {},
  options: ApiRequestOptions<TResponse> = {}
): Promise<TResponse> {
  const response = await send(path, init)
  const body = await readJson(response)

  if (response.ok) {
    return successBody(response.status, body, options)
  }

  const error = toApiError(response.status, body)

  if (!canRetryAfterUnauthorized(response.status, path, init, options)) {
    throw error
  }

  const decision = await askForRetry()

  if (!decision.retry) {
    throw decision.error ?? error
  }

  const retried = await send(path, init)
  const retriedBody = await readJson(retried)

  if (retried.ok) {
    return successBody(retried.status, retriedBody, options)
  }

  throw toApiError(retried.status, retriedBody)
}

/**
 * Проверка успешного тела.
 *
 * `204` не валидируется — в ответе нет данных. Иначе guard из вызывающего кода
 * решает, можно ли отдавать тело в UI: битый `200`/`201` становится `ApiError`
 * с фактическим статусом, чтобы экран показал обычный fallback.
 */
function successBody<TResponse>(
  status: number,
  body: unknown,
  options: ApiRequestOptions<TResponse>
): TResponse {
  if (status === 204) {
    return body as TResponse
  }

  if (!options.validate || options.validate(body)) {
    return body as TResponse
  }

  throw new ApiError(status, null, t('Сервер вернул неожиданный ответ.'))
}

async function send(path: string, init: RequestInit): Promise<Response> {
  const { headers, ...requestInit } = init

  try {
    return await fetch(`${API_PREFIX}${path}`, {
      ...requestInit,
      // Браузерная ветка аутентификации живёт на HttpOnly-cookie.
      credentials: 'include',
      headers: {
        Accept: 'application/json',
        // `FormData` получает `Content-Type` с boundary от браузера: JSON-заголовок
        // сломал бы multipart, поэтому для него заголовок не ставим (§8).
        ...(requestInit.body === undefined || isFormData(requestInit.body)
          ? {}
          : { 'Content-Type': 'application/json' }),
        ...headers,
      },
    })
  } catch {
    throw new ApiError(0, null, t('Нет связи с сервером.'))
  }
}

/** `FormData` в тестовом/браузерном окружении есть всегда, но проверяем безопасно. */
function isFormData(body: BodyInit | null | undefined): body is FormData {
  return typeof FormData !== 'undefined' && body instanceof FormData
}

/** Повторяем только `401` на данных: вход, `/auth/me` и refresh продлевать нечего. */
function canRetryAfterUnauthorized(
  status: number,
  path: string,
  init: RequestInit,
  options: Pick<ApiRequestOptions<never>, 'retryOnUnauthorized'>
): boolean {
  if (status !== 401 || options.retryOnUnauthorized === false) {
    return false
  }

  if (path.startsWith('/auth/')) {
    return false
  }

  if (init.signal?.aborted) {
    return false
  }

  return (
    init.body === undefined ||
    init.body === null ||
    typeof init.body === 'string' ||
    isFormData(init.body)
  )
}

async function askForRetry(): Promise<UnauthorizedResult> {
  if (!unauthorizedHandler) {
    return { retry: false }
  }

  try {
    return await unauthorizedHandler()
  } catch {
    // Обработчик не должен бросать; исключение считаем отказом без повтора.
    return { retry: false }
  }
}

/** Нейтральный русский текст вместо `HTTP ${status}`: статус наружу не показывается. */
function genericErrorMessage(): string {
  return t('Запрос не выполнен.')
}

function toApiError(status: number, body: unknown): ApiError {
  const problem = isProblemDetails(body) ? body : null

  return new ApiError(
    status,
    problem,
    problem?.detail ?? genericErrorMessage()
  )
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

/**
 * Мусор в теле ошибки (HTML, `text/plain`, чужой JSON) не должен притворяться
 * ProblemDetails: иначе `hasCode` начнёт доверять случайному полю `errorCodes`.
 */
function isProblemDetails(value: unknown): value is ProblemDetails {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return false
  }

  const candidate = value as Record<string, unknown>

  if (candidate.status !== undefined && typeof candidate.status !== 'number') {
    return false
  }

  if (candidate.title !== undefined && typeof candidate.title !== 'string') {
    return false
  }

  if (candidate.detail !== undefined && typeof candidate.detail !== 'string') {
    return false
  }

  if (
    candidate.errorCodes !== undefined &&
    (!Array.isArray(candidate.errorCodes) ||
      !candidate.errorCodes.every((code) => typeof code === 'string'))
  ) {
    return false
  }

  if (candidate.errors !== undefined) {
    const errors = candidate.errors

    if (typeof errors !== 'object' || errors === null || Array.isArray(errors)) {
      return false
    }

    const messages = Object.values(errors as Record<string, unknown>)

    if (
      !messages.every(
        (value) =>
          Array.isArray(value) && value.every((item) => typeof item === 'string')
      )
    ) {
      return false
    }
  }

  return true
}
