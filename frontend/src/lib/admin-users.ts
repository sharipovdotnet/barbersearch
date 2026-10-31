import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'

/**
 * Админское чтение пользователей и действия с ними.
 *
 * Контракт отдельный от кабинета: сервер отдаёт email, телефон и статусы
 * подтверждений, которых нет в публичных ответах. Ошибки ветвятся по `status`
 * и `errorCodes`, английский `detail` наружу не показывается.
 */

/** Роли приходят строками — так же, как в остальных контрактах. */
export type AdminUserRole = 'Customer' | 'Barber' | 'Admin'

/** Русские подписи ролей: значения из API остаются английскими. */
export const ADMIN_USER_ROLE_LABELS: Record<AdminUserRole, string> = {
  Customer: 'Клиент',
  Barber: 'Барбер',
  Admin: 'Администратор',
}

/** Порядок в выборе роли: от клиента к администратору, как в домене. */
export const ADMIN_USER_ROLES: readonly AdminUserRole[] = [
  'Customer',
  'Barber',
  'Admin',
]

/** Статус доступа в строке списка и в карточке. */
export const ADMIN_USER_ACTIVITY_LABELS = {
  active: 'Активен',
  blocked: 'Заблокирован',
} as const

export type AdminUserStatusFilter = 'all' | 'active' | 'blocked' | 'unverified'

export const ADMIN_USER_STATUS_LABELS: Record<AdminUserStatusFilter, string> = {
  all: 'Все',
  active: 'Активные',
  blocked: 'Заблокированные',
  unverified: 'Без подтверждения email',
}

export type AdminUserRoleFilter = 'all' | AdminUserRole

export const ADMIN_USER_ROLE_FILTER_LABELS: Record<
  AdminUserRoleFilter,
  string
> = {
  all: 'Все роли',
  Customer: 'Клиенты',
  Barber: 'Барберы',
  Admin: 'Администраторы',
}

/** Строка списка: без контактов и метаданных входа — они в карточке. */
export type AdminUserRow = {
  id: string
  email: string
  firstName: string
  lastName: string
  role: AdminUserRole
  isActive: boolean
  isEmailVerified: boolean
  isPhoneVerified: boolean
  barberProfileId: string | null
  lastLoginAt: string | null
  createdAt: string
}

export type AdminUserBarberProfile = {
  id: string
  displayName: string
  isVerified: boolean
  isActive: boolean
}

export type AdminUserDetail = {
  id: string
  email: string
  firstName: string
  lastName: string
  phoneNumber: string | null
  avatarUrl: string | null
  role: AdminUserRole
  isActive: boolean
  isEmailVerified: boolean
  isPhoneVerified: boolean
  barberProfileId: string | null
  barberProfile: AdminUserBarberProfile | null
  lastLoginAt: string | null
  /** Конец временной блокировки после неудачных попыток входа. */
  lockoutEnd: string | null
  failedLoginAttempts: number
  createdAt: string
  updatedAt: string
}

export type AdminUsersPage = {
  items: AdminUserRow[]
  nextCursor: string | null
}

/** Размер страницы совпадает с дефолтом сервера. */
export const ADMIN_USERS_PAGE_SIZE = 20

/** Максимальная длина поисковой строки на сервере; длиннее он ответит 400. */
export const ADMIN_USER_SEARCH_MAX_LENGTH = 100

/** «Дмитрий Крылов» либо email, если имя не заполнено. */
export function getUserDisplayName(user: {
  firstName: string
  lastName: string
  email: string
}): string {
  const name = `${user.firstName} ${user.lastName}`.trim()
  return name === '' ? user.email : name
}

export async function fetchAdminUsers({
  status,
  role,
  search,
  cursor,
  signal,
}: {
  status: AdminUserStatusFilter
  role: AdminUserRoleFilter
  search?: string
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminUsersPage> {
  const query = new URLSearchParams({
    status,
    role,
    pageSize: String(ADMIN_USERS_PAGE_SIZE),
  })

  if (search) {
    query.set('search', search)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminUsersPage>(`/admin/users?${query.toString()}`, {
    signal,
  })
}

export async function fetchAdminUser(
  userId: string,
  signal?: AbortSignal
): Promise<AdminUserDetail> {
  return apiRequest<AdminUserDetail>(`/admin/users/${userId}`, { signal })
}

/** Ошибка списка: 403 обрабатывает страница, остальное показывает текст. */
export function describeAdminUserListError(
  error: unknown,
  fallback = 'Не удалось загрузить пользователей.'
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.hasCode('admin.users.cursor.invalid')) {
      return 'Список устарел. Обновите его и продолжите с начала.'
    }
  }

  return describeApiFailure(error, fallback)
}

/** Ошибка карточки по прямому URL. */
export function describeAdminUserDetailError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('admin.users.notFound')) {
      return 'Пользователь не найден. Возможно, ссылка устарела.'
    }
  }

  return describeApiFailure(error, 'Не удалось загрузить пользователя.')
}

/** 401: сессия закончилась, страница предложит войти снова. */
export function isUserUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}

/** Действие администратора с пользователем. */
export type AdminUserAction =
  | 'role'
  | 'block'
  | 'unblock'
  | 'verifyEmail'
  | 'verifyPhone'

export async function changeUserRole(
  userId: string,
  role: AdminUserRole
): Promise<void> {
  await mutateWithCsrf<void>(`/users/${userId}/role`, {
    method: 'PUT',
    body: JSON.stringify({ role }),
  })
}

/** Блокировка — это снятие активации, отдельного статуса «заблокирован» нет. */
export async function blockUser(userId: string): Promise<void> {
  await mutateWithCsrf<void>(`/users/${userId}/activation`, {
    method: 'DELETE',
  })
}

export async function unblockUser(userId: string): Promise<void> {
  await mutateWithCsrf<void>(`/users/${userId}/activation`, {
    method: 'POST',
  })
}

export async function verifyUserEmail(userId: string): Promise<void> {
  await mutateWithCsrf<void>(`/users/${userId}/email-verification`, {
    method: 'POST',
  })
}

export async function verifyUserPhone(userId: string): Promise<void> {
  await mutateWithCsrf<void>(`/users/${userId}/phone-verification`, {
    method: 'POST',
  })
}

export type CreateUserInput = {
  email: string
  firstName: string
  lastName: string
  role: AdminUserRole
  password: string
}

export async function createUser(
  input: CreateUserInput
): Promise<{ id: string }> {
  return mutateWithCsrf<{ id: string }>('/users', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/**
 * Ошибка действия: отдельные тексты для правил домена.
 *
 * Смена роли и блокировка завершают все сессии пользователя, а последнего
 * активного администратора сервис не отпускает — эти коды объясняют причину,
 * а не просто показывают «не удалось».
 */
export function describeAdminUserActionError(
  error: unknown,
  action: AdminUserAction
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 401) {
      return 'Сессия истекла — войдите снова.'
    }

    if (error.status === 404 || error.hasCode('admin.users.notFound')) {
      return 'Пользователь больше не существует. Обновите список.'
    }

    if (error.hasCode('user.role.self')) {
      return 'Нельзя изменить роль своему аккаунту.'
    }

    if (error.hasCode('user.deactivation.self')) {
      return 'Нельзя заблокировать свой аккаунт.'
    }

    if (error.hasCode('user.admin.last')) {
      return 'Это последний активный администратор. Сначала назначьте другого, иначе сервис останется без управления.'
    }

    if (error.hasCode('user.update.concurrencyConflict')) {
      return 'Данные пользователя только что изменил кто-то другой. Обновите карточку и повторите.'
    }

    if (error.hasCode('auth.csrf.invalid')) {
      return 'Сессия устарела. Обновите страницу и повторите действие.'
    }

    if (error.hasCode('user.markPhoneAsVerified.phoneNumber')) {
      return 'Нельзя подтвердить телефон: он не указан.'
    }

    if (error.status === 429) {
      return 'Слишком много действий подряд. Подождите минуту и повторите.'
    }
  }

  switch (action) {
    case 'role':
      return 'Не удалось изменить роль. Попробуйте ещё раз.'
    case 'block':
      return 'Не удалось заблокировать пользователя. Попробуйте ещё раз.'
    case 'unblock':
      return 'Не удалось разблокировать пользователя. Попробуйте ещё раз.'
    case 'verifyEmail':
      return 'Не удалось подтвердить email. Попробуйте ещё раз.'
    case 'verifyPhone':
      return 'Не удалось подтвердить телефон. Попробуйте ещё раз.'
  }
}

/** Коды политики пароля приходят с префиксом `auth.password.`. */
function hasPasswordPolicyCode(error: ApiError): boolean {
  return error.codes.some((code) => code.startsWith('auth.password.'))
}

/** Ошибка создания: уникальность email и политика пароля — самые частые. */
export function describeCreateUserError(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'Не удалось создать пользователя. Попробуйте ещё раз.'
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  if (error.hasCode('user.add.uniqueViolation')) {
    return 'Пользователь с таким email уже есть.'
  }

  if (error.hasCode('user.email')) {
    return 'Похоже, в адресе email опечатка.'
  }

  if (hasPasswordPolicyCode(error)) {
    return 'Пароль не соответствует требованиям безопасности: от 12 до 128 символов.'
  }

  if (error.hasCode('admin.users.role')) {
    return 'Выберите роль из списка.'
  }

  if (error.status === 429) {
    return 'Слишком много действий подряд. Подождите минуту и повторите.'
  }

  return describeApiFailure(
    error,
    'Не удалось создать пользователя. Попробуйте ещё раз.'
  )
}
