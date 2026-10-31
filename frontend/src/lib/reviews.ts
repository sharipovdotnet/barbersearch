import { apiRequest } from '@/lib/api'
import type { Page } from '@/lib/paging'
import { isReviewPage } from '@/lib/response-guards'

/**
 * Публичные отзывы барбера.
 *
 * Контракт `GET api/reviews` отдаёт только идентификаторы и текст: имени, аватара,
 * услуги и времени визита в DTO нет, поэтому UI их не придумывает. `nextCursor`
 * непрозрачен и возвращается на сервер как есть.
 */

export type Review = {
  id: string
  barberProfileId: string
  /** Внутренний идентификатор автора: в DOM не выводится. */
  userId: string
  /** Внутренний идентификатор брони: в DOM не выводится. */
  bookingId: string
  rating: number
  text: string | null
  /** В публичной выдаче всегда `true`. */
  isPublished: boolean
  createdAt: string
  updatedAt: string
}

/** Десять карточек на страницу: кнопка «Показать ещё» не должна быть редкой. */
export const REVIEWS_PAGE_SIZE = 10

export async function fetchBarberReviews(
  barberProfileId: string,
  cursor?: string | null,
  signal?: AbortSignal
): Promise<Page<Review>> {
  const query = new URLSearchParams({
    barberProfileId,
    pageSize: String(REVIEWS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  // Чтение без CSRF: токен нужен только мутациям.
  return apiRequest<Page<Review>>(
    `/reviews?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isReviewPage }
  )
}
