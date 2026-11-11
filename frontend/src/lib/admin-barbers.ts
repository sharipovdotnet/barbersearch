import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import {
  describeAvatarModerationError,
  type AvatarModerationAction,
} from '@/lib/avatar-moderation'
import type { BarberGrade } from '@/lib/barber-grade'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { AdminBarberStatusFilter } from '@/lib/admin-path'
import type { PortfolioPhoto } from '@/lib/portfolio-photos'
import { isAdminBarber, isAdminBarberPage } from '@/lib/response-guards'

// Блокировка живёт в транспорте портфолио: здесь только точка импорта для админской
// карточки, чтобы не заводить второй экземпляр запроса.
export {
  blockPortfolioPhoto,
  unblockPortfolioPhoto,
} from '@/lib/portfolio-photos'

// Аватар принадлежит пользователю, поэтому запрос общий с карточкой пользователя:
// здесь только реэкспорт, второго экземпляра `POST/DELETE .../avatar/block` нет.
export { blockUserAvatar, unblockUserAvatar } from '@/lib/admin-users'

/**
 * Админское чтение барберов и словари для экрана.
 *
 * Контракт отдельный от публичной карточки: сервер отдаёт email владельца и профили,
 * которых нет в поиске. Ошибки ветвятся по `status` и `errorCodes`, английский `detail`
 * наружу не показывается.
 */

// Админские компоненты исторически импортируют словари отсюда; источник — общий
// `barber-grade.ts`, потому что грейды нужны и витрине.
export { BARBER_GRADE_LABELS, BARBER_GRADES } from '@/lib/barber-grade'
export type { BarberGrade } from '@/lib/barber-grade'

// Фильтр — часть адреса раздела (`?status=`), поэтому тип живёт в `admin-path`;
// здесь он остаётся точкой импорта для экрана и словарей.
export type { AdminBarberStatusFilter } from '@/lib/admin-path'

export const ADMIN_BARBER_STATUS_LABELS: Record<
  AdminBarberStatusFilter,
  string
> = {
  all: 'Все',
  pending: 'На проверке',
  hidden: 'Скрытые',
}

export type AdminBarber = {
  id: string
  displayName: string
  email: string
  /** Сырой URL владельца: админ видит и скрытое администратором фото. */
  avatarUrl: string | null
  isAvatarBlocked: boolean
  grade: BarberGrade
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
  createdAt: string
  updatedAt: string
}

/** Чек-лист готовности: подсказка, а не блокировка подтверждения. */
export type AdminBarberReadiness = {
  isProfileFilled: boolean
  isEmailVerified: boolean
  servicesCount: number
  salonsCount: number
  scheduleDaysCount: number
  photosCount: number
}

export type AdminBarberDetail = {
  id: string
  userId: string
  displayName: string
  email: string
  phoneNumber: string | null
  avatarUrl: string | null
  /** Флаг модерации: карточка показывает бейдж и кнопку «Вернуть фото». */
  isAvatarBlocked: boolean
  grade: BarberGrade
  bio: string | null
  yearsOfExperience: number
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
  readiness: AdminBarberReadiness
  /** Все фотографии профиля: админ видит и скрытые, и заблокированные. */
  photos: PortfolioPhoto[]
  createdAt: string
  updatedAt: string
}

export type AdminBarberPage = {
  items: AdminBarber[]
  nextCursor: string | null
}

/** Размер страницы совпадает с дефолтом сервера. */
export const ADMIN_BARBERS_PAGE_SIZE = 20

/** Максимальная длина поисковой строки на сервере; длиннее он ответит 400. */
export const ADMIN_BARBER_SEARCH_MAX_LENGTH = 100

export async function fetchAdminBarbers({
  status,
  search,
  cursor,
  signal,
}: {
  status: AdminBarberStatusFilter
  search?: string
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminBarberPage> {
  const query = new URLSearchParams({
    status,
    pageSize: String(ADMIN_BARBERS_PAGE_SIZE),
  })

  if (search) {
    query.set('search', search)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminBarberPage>(
    `/admin/barbers?${query.toString()}`,
    { signal },
    { validate: isAdminBarberPage }
  )
}

export async function fetchAdminBarber(
  barberId: string,
  signal?: AbortSignal
): Promise<AdminBarberDetail> {
  return apiRequest<AdminBarberDetail>(`/admin/barbers/${barberId}`, { signal }, {
    validate: isAdminBarber,
  })
}

/** Ошибка списка: 403 обрабатывает страница, остальное показывает текст. */
export function describeAdminBarberListError(
  error: unknown,
  fallback = 'Не удалось загрузить барберов.'
): string {
  if (error instanceof ApiError && error.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  return describeApiFailure(error, fallback)
}

/** Ошибка карточки по прямому URL. */
export function describeAdminBarberDetailError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return t('Раздел доступен только администратору.')
    }

    if (error.status === 404 || error.hasCode('barberProfile.notFound')) {
      return t('Профиль не найден. Возможно, ссылка устарела.')
    }
  }

  return describeApiFailure(error, 'Не удалось загрузить профиль барбера.')
}

/** 401: сессия закончилась, страница предложит войти снова. */
export function isBarberUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}

/** Действие администратора с профилем барбера. */
export type AdminBarberAction =
  | 'verify'
  | 'unverify'
  | 'grade'
  | 'hide'
  | 'show'
  | AvatarModerationAction

export async function verifyBarber(barberId: string): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberId}/verification`, {
    method: 'POST',
  })
}

export async function unverifyBarber(barberId: string): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberId}/verification`, {
    method: 'DELETE',
  })
}

export async function changeBarberGrade(
  barberId: string,
  grade: BarberGrade
): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberId}/grade`, {
    method: 'PUT',
    body: JSON.stringify({ grade }),
  })
}

/** «Скрыть из поиска» — выключение профиля, а не новый статус домена. */
export async function hideBarberFromSearch(barberId: string): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberId}/activation`, {
    method: 'POST',
  })
}

export async function returnBarberToSearch(barberId: string): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberId}/activation`, {
    method: 'DELETE',
  })
}

/**
 * Ошибка действия: отдельные тексты для скрытого профиля и истёкшей сессии.
 *
 * Подтвердить профиль, скрытый из поиска, нельзя — это правило домена
 * (`barberProfile.verify.inactive`), поэтому подсказка ведёт к возврату в поиск.
 */
export function describeAdminBarberActionError(
  error: unknown,
  action: AdminBarberAction
): string {
  // Аватар принадлежит владельцу профиля и блокируется тем же эндпоинтом, что
  // и в карточке пользователя: тексты не дублируем.
  if (action === 'blockAvatar' || action === 'unblockAvatar') {
    return describeAvatarModerationError(error, action)
  }

  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return t('Раздел доступен только администратору.')
    }

    if (error.status === 404 || error.hasCode('barberProfile.notFound')) {
      return t('Профиль больше не существует. Обновите список.')
    }

    if (error.hasCode('barberProfile.verify.inactive')) {
      return t(
        'Нельзя подтвердить профиль, скрытый из поиска. Сначала верните его в поиск.'
      )
    }

    if (error.hasCode('auth.csrf.invalid')) {
      return t('Сессия устарела. Обновите страницу и повторите действие.')
    }
  }

  switch (action) {
    case 'verify':
      return t('Не удалось подтвердить профиль. Попробуйте ещё раз.')
    case 'unverify':
      return t('Не удалось снять подтверждение. Попробуйте ещё раз.')
    case 'grade':
      return t('Не удалось изменить грейд. Попробуйте ещё раз.')
    case 'hide':
      return t('Не удалось скрыть профиль из поиска. Попробуйте ещё раз.')
    case 'show':
      return t('Не удалось вернуть профиль в поиск. Попробуйте ещё раз.')
  }
}
