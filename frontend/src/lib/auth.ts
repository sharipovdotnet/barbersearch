import { ApiError, apiRequest } from '@/lib/api'
import { fetchCsrfToken } from '@/lib/csrf'

/**
 * Браузерная аутентификация через cookie.
 *
 * Вход идёт в `POST api/auth/login/cookie`: сервер ставит HttpOnly-cookie сессии и refresh-токена
 * и ничего не возвращает в теле. Токены в JavaScript не попадают, поэтому после входа клиент
 * узнаёт пользователя из `GET api/auth/me`.
 *
 * Cookie-ветка защищена от CSRF: перед каждым изменяющим запросом берём токен из
 * `GET api/auth/csrf` и отправляем его в заголовке `X-CSRF-TOKEN`.
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

const CSRF_HEADER = 'X-CSRF-TOKEN'

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
  const csrfToken = await fetchCsrfToken()

  await apiRequest<void>('/auth/login/cookie', {
    method: 'POST',
    headers: { [CSRF_HEADER]: csrfToken },
    body: JSON.stringify(credentials),
  })

  const user = await fetchSession()
  if (!user) {
    throw new ApiError(401, null, 'Сервер не подтвердил вход.')
  }

  return user
}

export async function signOut(): Promise<void> {
  const csrfToken = await fetchCsrfToken()

  await apiRequest<void>('/auth/logout', {
    method: 'POST',
    headers: { [CSRF_HEADER]: csrfToken },
  })
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
  const csrfToken = await fetchCsrfToken()

  await apiRequest<void>('/auth/change-password', {
    method: 'POST',
    headers: { [CSRF_HEADER]: csrfToken },
    body: JSON.stringify({ currentPassword, newPassword }),
  })
}

/** Минимальная длина нового пароля — совпадает с PasswordPolicy.MIN_LENGTH на сервере. */
export const MIN_PASSWORD_LENGTH = 12

/** Верхняя граница совпадает с PasswordPolicy.MAX_LENGTH. */
export const MAX_PASSWORD_LENGTH = 128
