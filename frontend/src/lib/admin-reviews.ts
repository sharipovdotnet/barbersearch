import { ApiError, apiRequest } from '@/lib/api'
import { csrfHeaders } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { AdminTab } from '@/lib/admin-path'

/**
 * Админское чтение отзывов и действия модерации.
 *
 * Контракт отдельный от публичного `ReviewResponse`: сервер отдаёт имя барбера,
 * клиента и контекст визита. Ошибки ветвятся по `status` и `errorCodes`, английский
 * `detail` наружу не показывается.
 */

export type AdminReviewBarber = {
  id: string
  displayName: string
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
}

export type AdminReviewCustomer = {
  id: string
  fullName: string
  email: string
}

export type AdminReviewVisit = {
  bookingId: string
  startAt: string
  price: number
  serviceName: string
  salonName: string
  timeZoneId: string
}

export type AdminReview = {
  id: string
  rating: number
  text: string | null
  isPublished: boolean
  createdAt: string
  updatedAt: string
  barber: AdminReviewBarber
  customer: AdminReviewCustomer
  visit: AdminReviewVisit
}

export type AdminReviewPage = {
  items: AdminReview[]
  nextCursor: string | null
}

/** Размер страницы совпадает с дефолтом сервера. */
export const ADMIN_REVIEWS_PAGE_SIZE = 20

export async function fetchAdminReviews({
  tab,
  cursor,
  signal,
}: {
  tab: AdminTab
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminReviewPage> {
  const query = new URLSearchParams({
    status: tab,
    pageSize: String(ADMIN_REVIEWS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminReviewPage>(`/admin/reviews?${query.toString()}`, {
    signal,
  })
}

export async function fetchAdminReview(
  reviewId: string,
  signal?: AbortSignal
): Promise<AdminReview> {
  return apiRequest<AdminReview>(`/admin/reviews/${reviewId}`, { signal })
}

export async function publishReview(reviewId: string): Promise<void> {
  await apiRequest<void>(`/reviews/${reviewId}/publication`, {
    method: 'POST',
    headers: await csrfHeaders(),
  })
}

export async function hideReview(reviewId: string): Promise<void> {
  await apiRequest<void>(`/reviews/${reviewId}/publication`, {
    method: 'DELETE',
    headers: await csrfHeaders(),
  })
}

/** Ошибка списка: 401 обрабатывает страница, остальное показывает текст. */
export function describeAdminReviewListError(
  error: unknown,
  fallback = 'Не удалось загрузить отзывы.'
): string {
  if (error instanceof ApiError && error.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  return describeApiFailure(error, fallback)
}

/** Ошибка загрузки карточки по прямому URL. */
export function describeAdminReviewDetailError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('review.notFound')) {
      return 'Этот отзыв уже разобрали.'
    }
  }

  return describeApiFailure(error, 'Не удалось загрузить отзыв.')
}

/** Ошибка действия: отдельные тексты для уже разобранного отзыва и истёкшей сессии. */
export function describeAdminReviewActionError(
  error: unknown,
  action: 'publish' | 'hide'
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('review.notFound')) {
      return 'Этот отзыв уже не в очереди.'
    }

    if (error.hasCode('auth.csrf.invalid')) {
      return 'Сессия устарела. Обновите страницу и повторите действие.'
    }
  }

  return describeApiFailure(
    error,
    action === 'publish'
      ? 'Не удалось опубликовать отзыв. Попробуйте ещё раз.'
      : 'Не удалось скрыть отзыв. Попробуйте ещё раз.'
  )
}

/** 404 отзыва в действии: строку надо убрать из списка, а не показывать ошибку. */
export function isReviewGone(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    (error.status === 404 || error.hasCode('review.notFound'))
  )
}

/** 401 в действии: сессия закончилась, страница предложит войти снова. */
export function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}
