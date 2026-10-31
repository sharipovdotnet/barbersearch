import { apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import type { Page } from '@/lib/paging'

/**
 * Отзывы автора.
 *
 * `GET api/reviews/mine` отдаёт оба статуса и контекст визита, поэтому строке не нужны
 * дополнительные запросы к броням и профилю барбера. Даты и статусы здесь не
 * преобразуются: это делает UI через `format.ts`.
 */

export type MyReviewBarber = {
  id: string
  displayName: string
}

export type MyReviewVisit = {
  bookingId: string
  startAt: string
  timeZoneId: string
  price: number
  serviceName: string
  salonName: string
}

export type MyReview = {
  id: string
  rating: number
  text: string | null
  /** `false` — «на модерации»; скрытый администратором отзыв не отличается. */
  isPublished: boolean
  createdAt: string
  updatedAt: string
  barber: MyReviewBarber
  visit: MyReviewVisit
}

/** Десять строк на страницу: «Показать ещё» остаётся редкой кнопкой. */
export const MY_REVIEWS_PAGE_SIZE = 10

export async function fetchMyReviews(
  cursor?: string | null,
  signal?: AbortSignal
): Promise<Page<MyReview>> {
  const query = new URLSearchParams({
    pageSize: String(MY_REVIEWS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  // Чтение без CSRF: токен нужен только мутациям.
  return apiRequest<Page<MyReview>>(
    `/reviews/mine?${query.toString()}`,
    signal ? { signal } : {}
  )
}

export type UpdateMyReviewInput = {
  rating: number
  text: string | null
}

/** `204` без тела: `updatedAt` на клиенте после правки не обновляется. */
export async function updateMyReview(
  id: string,
  input: UpdateMyReviewInput
): Promise<void> {
  await mutateWithCsrf<void>(`/reviews/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/** Удаление только автором; повторный запрос по тому же id вернёт `404`. */
export async function deleteMyReview(id: string): Promise<void> {
  await mutateWithCsrf<void>(`/reviews/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  })
}
