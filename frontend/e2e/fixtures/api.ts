import { expect, type APIRequestContext } from '@playwright/test'

import { baseURL, storageStatePath } from './accounts'

/**
 * Подготовка состояния через API для мутирующих сцен.
 *
 * Удаление отзыва необратимо, поэтому тест запоминает, какие отзывы были, и после
 * удаления создаёт отзыв заново на освободившейся записи — счётчики страниц
 * остаются теми же для следующего прогона (§5: мутации не ломают соседние спеки).
 */
export type MyReviewDto = {
  id: string
  visit: { bookingId: string }
}

type Page = {
  items: MyReviewDto[]
  nextCursor: string | null
}

/** Все свои отзывы, сшивая страницы курсором. */
export async function fetchAllMyReviews(request: APIRequestContext): Promise<MyReviewDto[]> {
  const items: MyReviewDto[] = []
  let cursor: string | null = null

  do {
    const query = new URLSearchParams({ pageSize: '10' })
    if (cursor !== null) {
      query.set('cursor', cursor)
    }

    const response = await request.get(`/api/reviews/mine?${query.toString()}`)
    expect(response.status(), 'GET /api/reviews/mine').toBe(200)

    const body = (await response.json()) as Page
    items.push(...body.items)
    cursor = body.nextCursor
  } while (cursor !== null)

  return items
}

async function csrfToken(request: APIRequestContext): Promise<string> {
  const response = await request.get('/api/auth/csrf')
  expect(response.status(), 'GET /api/auth/csrf').toBe(200)

  const body = (await response.json()) as { token: string }

  return body.token
}

/** Возвращает id созданного отзыва. */
export async function createReview(
  request: APIRequestContext,
  bookingId: string,
  rating: number,
  text: string
): Promise<string> {
  const token = await csrfToken(request)

  const response = await request.post('/api/reviews', {
    headers: { 'X-CSRF-TOKEN': token },
    data: { bookingId, rating, text },
  })

  expect(response.status(), 'POST /api/reviews').toBe(201)

  const body = (await response.json()) as { id: string }

  return body.id
}

/** Вернуть число своих отзывов к исходному: по одному за каждый удалённый. */
export async function restoreDeletedReviews(
  request: APIRequestContext,
  before: MyReviewDto[]
): Promise<void> {
  const after = await fetchAllMyReviews(request)
  const keptBookings = new Set(after.map((review) => review.visit.bookingId))
  const freedBookings = before
    .map((review) => review.visit.bookingId)
    .filter((bookingId) => !keptBookings.has(bookingId))

  for (const bookingId of freedBookings) {
    await createReview(request, bookingId, 4, 'E2E отзыв после удаления')
  }
}

/** Вернуть отзыв в очередь модерации: снять публикацию (восстановление фикстур). */
export async function hidePublishedReview(
  request: APIRequestContext,
  reviewId: string
): Promise<void> {
  const token = await csrfToken(request)

  const response = await request.delete(
    `/api/reviews/${reviewId}/publication`,
    { headers: { 'X-CSRF-TOKEN': token } }
  )

  expect(
    response.status(),
    'DELETE /api/reviews/{id}/publication'
  ).toBe(204)
}

/**
 * Запись на будущее для восстановления «Ближайших» после терминального действия.
 *
 * Слот, который освобождает тест, лежит в будущем относительно seed, поэтому
 * создаём запись на свободные сутки дальше: тот же день мог быть занят прошлым
 * прогоном, отсюда перебор со сдвигом и повтор на `409`.
 *
 * Запись создаёт клиент, а не барбер: роль кабинета бронировать не может,
 * поэтому контекст берётся из storageState клиента.
 */
export type PlaywrightApi = {
  request: {
    newContext(options: {
      baseURL?: string
      storageState?: string
    }): Promise<APIRequestContext>
  }
}

export async function createUpcomingBooking(
  playwright: PlaywrightApi,
  ids: { barberProfileId: string; barberServiceId: string; salonId: string }
): Promise<void> {
  const context = await playwright.request.newContext({
    baseURL,
    storageState: storageStatePath('customer'),
  })

  try {
    // Контекст уже аутентифицирован storageState клиента, поэтому токен привязан к его
    // личности: анонимный токен получил бы 400 на первой же попытке.
    const token = await csrfToken(context)

    for (let dayOffset = 22; dayOffset < 30; dayOffset += 1) {
      const start = new Date()
      start.setUTCDate(start.getUTCDate() + dayOffset)
      start.setUTCHours(9, 0, 0, 0)

      const end = new Date(start.getTime() + 30 * 60_000)

      const response = await context.post('/api/bookings', {
        headers: { 'X-CSRF-TOKEN': token },
        data: {
          barberProfileId: ids.barberProfileId,
          barberServiceId: ids.barberServiceId,
          salonId: ids.salonId,
          startAt: start.toISOString(),
          endAt: end.toISOString(),
          // Ключ уникален для попытки: повтор с тем же ключом вернул бы уже
          // отменённую запись (idempotent replay) и не восстановил бы страницу.
          idempotencyKey: `e2e-restore-${start.toISOString()}-${Date.now()}`,
          customerNote: null,
        },
      })

      if (response.status() === 201) {
        return
      }

      expect([400, 409], `POST /api/bookings: ${response.status()}`).toContain(
        response.status()
      )
    }

    throw new Error('не удалось восстановить предстоящую запись для кабинета барбера')
  } finally {
    await context.dispose()
  }
}
