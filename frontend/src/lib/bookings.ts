import { apiRequest } from '@/lib/api'
import { csrfHeaders } from '@/lib/csrf'
import type { Page } from '@/lib/paging'

/**
 * Брони клиента.
 *
 * Контракт `GET api/bookings` отдаёт только идентификаторы связанных агрегатов —
 * имя барбера, услуга и салон догружаются из карточки барбера (`lib/catalog.ts`).
 * Пагинация курсорная: `nextCursor` возвращается как есть и не разбирается на клиенте.
 */

export type BookingStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Cancelled'
  | 'Completed'
  | 'NoShow'

export type Booking = {
  id: string
  userId: string
  barberProfileId: string
  barberServiceId: string
  salonId: string
  startAt: string
  endAt: string
  price: number
  status: BookingStatus
  idempotencyKey: string
  customerNote: string | null
  barberNote: string | null
  createdAt: string
}

/** Размер страницы совпадает с дефолтом сервера: кабинет не запрашивает больше. */
export const BOOKINGS_PAGE_SIZE = 20

export async function fetchBookings(
  cursor?: string | null
): Promise<Page<Booking>> {
  const query = new URLSearchParams({
    pageSize: String(BOOKINGS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<Page<Booking>>(`/bookings?${query.toString()}`)
}

/** Отмена возможна только для `Pending` и `Confirmed` — это же проверяет домен. */
export async function cancelBooking(bookingId: string): Promise<void> {
  await apiRequest<void>(`/bookings/${bookingId}/cancellation`, {
    method: 'POST',
    headers: await csrfHeaders(),
  })
}

export type CreateReviewInput = {
  bookingId: string
  rating: number
  text: string | null
}

/**
 * Отзыв создаётся скрытым и уходит на модерацию: рейтинг барбера меняется
 * только после публикации администратором.
 */
export async function createReview(input: CreateReviewInput): Promise<string> {
  const created = await apiRequest<{ id: string }>('/reviews', {
    method: 'POST',
    headers: await csrfHeaders(),
    body: JSON.stringify(input),
  })

  return created.id
}
