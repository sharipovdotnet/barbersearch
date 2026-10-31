import { ApiError, apiRequest } from '@/lib/api'
import { CSRF_HEADER, fetchCsrfToken, mutateWithCsrf } from '@/lib/csrf'

/**
 * Браузерная аутентификация через cookie.
 *
 * Вход идёт в `POST api/auth/login/cookie`: сервер ставит HttpOnly-cookie сессии и refresh-токена
 * и ничего не возвращает в теле. Токены в JavaScript не попадают, поэтому после входа клиент
 * узнаёт пользователя из `GET api/auth/me`.
 *
 * Cookie-ветка защищена от CSRF: изменяющие запросы идут через `mutateWithCsrf` — свежий
 * токен из `GET api/auth/csrf` в заголовке `X-CSRF-TOKEN` и один повтор на устаревший.
 */

export type SessionUser = {
  id: string
  email: string
  role: string
  barberProfileId: string | null
  isEmailVerified: boolean
}

export type SignInCredentials = {
  email: string
  password: string
  rememberMe: boolean
}

export type RegistrationInput = {
  email: string
  password: string
  firstName: string
  lastName: string
}

/** Текущий пользователь или `null`, если cookie-сессии нет. */
export async function fetchSession(): Promise<SessionUser | null> {
  try {
    return await apiRequest<SessionUser>('/auth/me')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) {
      return null
    }

    throw error
  }
}

export async function signIn(
  credentials: SignInCredentials
): Promise<SessionUser> {
  // У входа токен ещё анонимный — это нормально, повтор возьмёт новый анонимный.
  await mutateWithCsrf<void>('/auth/login/cookie', {
    method: 'POST',
    body: JSON.stringify(credentials),
  })

  const user = await fetchSession()
  if (!user) {
    throw new ApiError(401, null, 'Сервер не подтвердил вход.')
  }

  return user
}

/**
 * Один `POST /auth/refresh` в cookie-ветке: свежий CSRF → `204` + новые cookie.
 *
 * Тело не отправляется: сервер берёт refresh-токен из HttpOnly-cookie, а antiforgery
 * проверяет по заголовку. Повтор на `401` выключен — этим вызовом продлевает сессию
 * оркестратор `lib/session-refresh.ts`, а не транспорт.
 */
export async function requestSessionRefresh(): Promise<void> {
  const csrfToken = await fetchCsrfToken()

  await apiRequest<void>(
    '/auth/refresh',
    {
      method: 'POST',
      headers: { [CSRF_HEADER]: csrfToken },
    },
    { retryOnUnauthorized: false }
  )
}

export async function signOut(): Promise<void> {
  await mutateWithCsrf<void>('/auth/logout', { method: 'POST' })
}

/**
 * Самостоятельная регистрация клиента; возвращает id созданного пользователя.
 *
 * Это второе исключение по CSRF после создания брони: у действия нет antiforgery-фильтра,
 * поэтому токен не запрашивается и заголовок не отправляется. Аккаунт создаёт только
 * регистрация — вход в него остаётся обычным путём `signIn` (CSRF → login → `/auth/me`).
 */
export async function register(input: RegistrationInput): Promise<string> {
  const created = await apiRequest<{ id: string }>('/auth/register', {
    method: 'POST',
    body: JSON.stringify(input),
  })

  return created.id
}

/**
 * Смена пароля вошедшим пользователем.
 *
 * Пользователь берётся из cookie, но запрос всё равно требует antiforgery-токен:
 * иначе чужой сайт мог бы сменить пароль от имени жертвы. Сервер отзывает все
 * refresh-токены, поэтому после успешного вызова сессия остаётся только текущей.
 */
export async function changePassword(
  currentPassword: string,
  newPassword: string
): Promise<void> {
  await mutateWithCsrf<void>('/auth/change-password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword }),
  })
}

/** Минимальная длина нового пароля — совпадает с PasswordPolicy.MIN_LENGTH на сервере. */
export const MIN_PASSWORD_LENGTH = 12

/** Верхняя граница совпадает с PasswordPolicy.MAX_LENGTH. */
export const MAX_PASSWORD_LENGTH = 128
