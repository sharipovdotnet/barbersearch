import { apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import type { Page } from '@/lib/paging'
import { isBookingPage, isCreatedId } from '@/lib/response-guards'

/**
 * Брони клиента.
 *
 * Контракт `GET api/bookings` отдаёт только идентификаторы связанных агрегатов —
 * имя барбера, услуга и салон догружаются из карточки барбера (`lib/catalog.ts`).
 * Пагинация курсорная: `nextCursor` возвращается как есть и не разбирается на клиенте.
 * Скоуп вкладок определяет сервер: `all` (по умолчанию), `upcoming` и `past` — разные
 * выборки, у каждой свой курсор и своё направление сравнения.
 */

export type BookingStatus =
  | 'Pending'
  | 'Confirmed'
  | 'Cancelled'
  | 'Completed'
  | 'NoShow'

/** Скоуп списка броней; `all` — прежнее поведение и значение по умолчанию. */
export type BookingScope = 'all' | 'upcoming' | 'past'

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
  scope: BookingScope = 'all',
  cursor: string | null = null,
  signal?: AbortSignal
): Promise<Page<Booking>> {
  const query = new URLSearchParams({
    scope,
    pageSize: String(BOOKINGS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<Page<Booking>>(
    `/bookings?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isBookingPage }
  )
}

/** Отмена возможна только для `Pending` и `Confirmed` — это же проверяет домен. */
export async function cancelBooking(bookingId: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${bookingId}/cancellation`, {
    method: 'POST',
  })
}

export type CreateBookingInput = {
  barberProfileId: string
  barberServiceId: string
  salonId: string
  startAt: string
  endAt: string
  idempotencyKey: string
  customerNote: string | null
}

/**
 * Создание брони; возвращает `id` созданной записи.
 *
 * `startAt`/`endAt` отправляются ровно такими, какими пришли из `/slots`: сервер не
 * сверяет время с расписанием, поэтому пересчёт длительности или пояса создал бы
 * бронь не на тот слот. С #35 это cookie-мутация, поэтому запрос идёт через
 * `mutateWithCsrf`: один повтор на `auth.csrf.invalid` безопасен, потому что фильтр
 * отклоняет запрос до действия, а `idempotencyKey` в теле не меняется.
 */
export async function createBooking(
  input: CreateBookingInput
): Promise<string> {
  const created = await mutateWithCsrf<{ id: string }>('/bookings', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })

  return created.id
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
  const created = await mutateWithCsrf<{ id: string }>('/reviews', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })

  return created.id
}
