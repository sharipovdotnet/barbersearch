import { ApiError, apiRequest } from '@/lib/api'
import type { BarberGrade } from '@/lib/barber-grade'
import { csrfHeaders } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'

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

export type AdminBarberStatusFilter = 'all' | 'pending' | 'hidden'

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
  grade: BarberGrade
  bio: string | null
  yearsOfExperience: number
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
  readiness: AdminBarberReadiness
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

  return apiRequest<AdminBarberPage>(`/admin/barbers?${query.toString()}`, {
    signal,
  })
}

export async function fetchAdminBarber(
  barberId: string,
  signal?: AbortSignal
): Promise<AdminBarberDetail> {
  return apiRequest<AdminBarberDetail>(`/admin/barbers/${barberId}`, { signal })
}

/** Ошибка списка: 403 обрабатывает страница, остальное показывает текст. */
export function describeAdminBarberListError(
  error: unknown,
  fallback = 'Не удалось загрузить барберов.'
): string {
  if (error instanceof ApiError && error.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  return describeApiFailure(error, fallback)
}

/** Ошибка карточки по прямому URL. */
export function describeAdminBarberDetailError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('barberProfile.notFound')) {
      return 'Профиль не найден. Возможно, ссылка устарела.'
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

export async function verifyBarber(barberId: string): Promise<void> {
  await apiRequest<void>(`/barber-profiles/${barberId}/verification`, {
    method: 'POST',
    headers: await csrfHeaders(),
  })
}

export async function unverifyBarber(barberId: string): Promise<void> {
  await apiRequest<void>(`/barber-profiles/${barberId}/verification`, {
    method: 'DELETE',
    headers: await csrfHeaders(),
  })
}

export async function changeBarberGrade(
  barberId: string,
  grade: BarberGrade
): Promise<void> {
  await apiRequest<void>(`/barber-profiles/${barberId}/grade`, {
    method: 'PUT',
    body: JSON.stringify({ grade }),
    headers: await csrfHeaders(),
  })
}

/** «Скрыть из поиска» — выключение профиля, а не новый статус домена. */
export async function hideBarberFromSearch(barberId: string): Promise<void> {
  await apiRequest<void>(`/barber-profiles/${barberId}/activation`, {
    method: 'POST',
    headers: await csrfHeaders(),
  })
}

export async function returnBarberToSearch(barberId: string): Promise<void> {
  await apiRequest<void>(`/barber-profiles/${barberId}/activation`, {
    method: 'DELETE',
    headers: await csrfHeaders(),
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
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('barberProfile.notFound')) {
      return 'Профиль больше не существует. Обновите список.'
    }

    if (error.hasCode('barberProfile.verify.inactive')) {
      return 'Нельзя подтвердить профиль, скрытый из поиска. Сначала верните его в поиск.'
    }

    if (error.hasCode('auth.csrf.invalid')) {
      return 'Сессия устарела. Обновите страницу и повторите действие.'
    }
  }

  switch (action) {
    case 'verify':
      return 'Не удалось подтвердить профиль. Попробуйте ещё раз.'
    case 'unverify':
      return 'Не удалось снять подтверждение. Попробуйте ещё раз.'
    case 'grade':
      return 'Не удалось изменить грейд. Попробуйте ещё раз.'
    case 'hide':
      return 'Не удалось скрыть профиль из поиска. Попробуйте ещё раз.'
    case 'show':
      return 'Не удалось вернуть профиль в поиск. Попробуйте ещё раз.'
  }
}
