import { ApiError, apiRequest } from '@/lib/api'
import { isCsrfToken } from '@/lib/response-guards'

/**
 * Antiforgery-токен для cookie-мутаций.
 *
 * Токен привязан к текущей личности и не кэшируется: после входа старый становится
 * недействительным, поэтому каждый изменяющий запрос берёт свежий.
 */
type CsrfToken = {
  token: string
}

const CSRF_HEADER = 'X-CSRF-TOKEN'

export { CSRF_HEADER }

export async function fetchCsrfToken(): Promise<string> {
  const { token } = await apiRequest<CsrfToken>('/auth/csrf', {}, {
    validate: isCsrfToken,
  })
  return token
}

/**
 * Мутация с antiforgery-токеном и одним повтором на устаревший токен.
 *
 * `auth.csrf.invalid` формирует antiforgery-фильтр **до** действия, поэтому первая
 * попытка не изменила данные и повтор безопасен; тело — строка, её можно отправить
 * снова. Второй `400` уходит наверх без третьего запроса, остальные ошибки не
 * повторяются вовсе.
 */
export async function mutateWithCsrf<TResponse>(
  path: string,
  init: MutationInit<TResponse> = {}
): Promise<TResponse> {
  const { validate, ...requestInit } = init
  const token = await fetchCsrfToken()

  try {
    return await apiRequest<TResponse>(path, withCsrf(requestInit, token), {
      validate,
    })
  } catch (error) {
    if (!isStaleCsrf(error, init.signal)) {
      throw error
    }
  }

  const freshToken = await fetchCsrfToken()

  return apiRequest<TResponse>(path, withCsrf(requestInit, freshToken), {
    validate,
  })
}

type MutationInit<TResponse> = Omit<RequestInit, 'headers'> & {
  headers?: Record<string, string>
  /** Проверка успешного тела; у `204` проверять нечего. */
  validate?: (body: unknown) => body is TResponse
}

type MutationRequestInit = Omit<RequestInit, 'headers'> & {
  headers?: Record<string, string>
}

function withCsrf(
  init: MutationRequestInit,
  token: string
): RequestInit {
  return {
    ...init,
    headers: { ...init.headers, [CSRF_HEADER]: token },
  }
}

/** Отменённый запрос не повторяем: токен тут ни при чём. */
function isStaleCsrf(error: unknown, signal?: AbortSignal | null): boolean {
  return (
    error instanceof ApiError &&
    error.status === 400 &&
    error.hasCode('auth.csrf.invalid') &&
    signal?.aborted !== true
  )
}
