import { randomUUID } from 'node:crypto'

import { expect, type APIRequestContext } from '@playwright/test'

import { baseURL, fixtures, storageStatePath } from './accounts'

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

/**
 * Мутация админского каталога через API — восстановление фикстур в `finally`.
 *
 * Каталог правится теми же эндпоинтами, что и публичный: активация, правка строки,
 * контакты салона. Нас интересует только результат (`2xx`), потому что тест уже проверил
 * ветку интерфейса; повторная активация идемпотентна, поэтому вызов безопасен, даже если
 * строка уже в нужном состоянии.
 */
export async function adminCatalogueMutation(
  playwright: PlaywrightApi,
  method: 'POST' | 'PUT' | 'DELETE',
  path: string,
  data?: unknown
): Promise<void> {
  const context = await playwright.request.newContext({
    baseURL,
    storageState: storageStatePath('admin'),
  })

  try {
    const token = await csrfToken(context)

    const response = await context.fetch(path, {
      method,
      headers: { 'X-CSRF-TOKEN': token },
      ...(data === undefined ? {} : { data }),
    })

    expect(
      [200, 201, 204],
      `${method} ${path}: ${response.status()}`
    ).toContain(response.status())
  } finally {
    await context.dispose()
  }
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

/**
 * Уборка аватара после упавшей сцены: `DELETE` идемпотентен и отвечает `204`,
 * даже когда файла уже нет. `request` приходит из теста, поэтому удаляется аватар
 * того аккаунта, чей `storageState` у сценария.
 */
export async function deleteMyAvatar(
  request: APIRequestContext
): Promise<void> {
  const csrf = await request.get('/api/auth/csrf')

  if (csrf.status() !== 200) {
    return
  }

  const { token } = (await csrf.json()) as { token: string }

  const response = await request.delete('/api/users/me/avatar', {
    headers: { 'X-CSRF-TOKEN': token },
  })

  expect(
    [204, 401],
    `DELETE /api/users/me/avatar: ${response.status()}`
  ).toContain(response.status())
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
 * Отвязывает салон от профиля барбера — откат мутирующего спека «Мои салоны».
 *
 * UI среза удаление салона не умеет (оно осталось в API), поэтому `finally` зовёт
 * существующий `DELETE /api/barber-profiles/{id}/salons/{salonId}` от имени барбера.
 * `404` допустим: параллельный прогон уже удалил связь.
 */
export async function detachBarberSalon(
  playwright: PlaywrightApi,
  ids: { barberProfileId: string; salonId: string }
): Promise<void> {
  const context = await playwright.request.newContext({
    baseURL,
    storageState: storageStatePath('barber'),
  })

  try {
    const token = await csrfToken(context)

    const response = await context.delete(
      `/api/barber-profiles/${ids.barberProfileId}/salons/${ids.salonId}`,
      { headers: { 'X-CSRF-TOKEN': token } }
    )

    expect(
      [204, 404],
      `DELETE barber salon: ${response.status()}`
    ).toContain(response.status())
  } finally {
    await context.dispose()
  }
}

/**
 * Минимальный срез `Playwright`-фикстуры: контекст запросов с чужим storageState.
 */
export type PlaywrightApi = {
  request: {
    newContext(options: {
      baseURL?: string
      storageState?: string
    }): Promise<APIRequestContext>
  }
}

/**
 * Гостевая запись витринного барбера на конкретный момент.
 *
 * Гость не создаёт аккаунт: кабинеты клиентов и поиск админских броней по email
 * такие строки не видят, поэтому чужие счётчики от фикстуры ленты дня не меняются.
 * Удаления брони в API нет — `finally` отменяет созданное, это единственная
 * доступная уборка.
 */
export type GuestBooking = {
  id: string
  /** Фактическое начало: пересечение с прошлым прогоном сдвигает время. */
  startAt: string
  /** Как имя гостя выглядит в строке кабинета. */
  guestName: string
}

export type GuestBookingRequest = {
  startAt: Date
  /** Фамилия гостя: по ней тест находит свою строку в ленте. */
  lastName: string
}

/** Полчаса — шаг сдвига при `409`: сдвиг не должен выйти за пределы дня. */
const GUEST_SHIFT_MINUTES = 30
const GUEST_MAX_ATTEMPTS = 4

/**
 * Создаёт гостевые записи через кабинет барбера.
 *
 * `POST /api/barber/bookings` проверяет только окно начала (`BOOKING_START_TOLERANCE_MINUTES`),
 * а не расписание, поэтому время можно выбрать свободно. Пересечение отменяет
 * попытку `409`-ом: время сдвигается на полчаса, но не дальше конца суток.
 */
export async function createGuestBookings(
  playwright: PlaywrightApi,
  requests: readonly GuestBookingRequest[]
): Promise<GuestBooking[]> {
  const context = await playwright.request.newContext({
    baseURL,
    storageState: storageStatePath('barber'),
  })

  const created: GuestBooking[] = []

  try {
    const token = await csrfToken(context)

    for (const request of requests) {
      const booking = await createGuestBooking(context, token, request)

      if (booking === null) {
        throw new Error(
          `не удалось создать запись «${request.lastName}» на ${request.startAt.toISOString()}`
        )
      }

      created.push(booking)
    }
  } finally {
    await context.dispose()
  }

  return created
}

async function createGuestBooking(
  context: APIRequestContext,
  token: string,
  request: GuestBookingRequest
): Promise<GuestBooking | null> {
  const endOfDay = new Date(request.startAt)
  endOfDay.setHours(23, 59, 59, 0)

  for (let attempt = 0; attempt < GUEST_MAX_ATTEMPTS; attempt += 1) {
    const start = new Date(
      request.startAt.getTime() + attempt * GUEST_SHIFT_MINUTES * 60_000
    )

    if (start.getTime() >= endOfDay.getTime()) {
      return null
    }

    const end = new Date(start.getTime() + 30 * 60_000)

    const response = await context.post('/api/barber/bookings', {
      headers: { 'X-CSRF-TOKEN': token },
      data: {
        customerId: null,
        guestFirstName: 'E2E',
        guestLastName: request.lastName,
        guestPhone: '+79990000000',
        barberServiceId: fixtures.barberServiceId,
        salonId: fixtures.salonId,
        startAt: start.toISOString(),
        endAt: end.toISOString(),
        idempotencyKey: `e2e-day-${randomUUID()}`,
        customerNote: null,
      },
    })

    if (response.status() === 201) {
      const body = (await response.json()) as { id: string }

      return {
        id: body.id,
        startAt: start.toISOString(),
        guestName: `E2E ${request.lastName}`,
      }
    }

    expect(
      [400, 409],
      `POST /api/barber/bookings: ${response.status()}`
    ).toContain(response.status())
  }

  return null
}

/**
 * Отмена созданных гостевых записей — уборка фикстур ленты дня.
 *
 * Барбер обязан указать причину, а иначе сервер ответит `400`. `409`/`404` терпим:
 * параллельный прогон мог уже завершить или отменить строку.
 */
export async function cancelGuestBookings(
  playwright: PlaywrightApi,
  ids: readonly string[]
): Promise<void> {
  if (ids.length === 0) {
    return
  }

  const context = await playwright.request.newContext({
    baseURL,
    storageState: storageStatePath('barber'),
  })

  try {
    const token = await csrfToken(context)

    for (const id of ids) {
      const response = await context.post(`/api/bookings/${id}/cancellation`, {
        headers: { 'X-CSRF-TOKEN': token },
        data: { reason: 'E2E: уборка фикстур ленты дня' },
      })

      expect(
        [204, 404, 409],
        `POST /api/bookings/{id}/cancellation: ${response.status()}`
      ).toContain(response.status())
    }
  } finally {
    await context.dispose()
  }
}

/**
 * Отмена записей с меткой прогона в имени гостя — уборка спека переноса.
 *
 * Импорт создаёт записи из интерфейса, поэтому их id тесту неизвестны: после
 * прогона находим их в «Ближайших» барбера по фамилии и отменяем. Контекст
 * запросов идёт из браузерной сессии (`page.request`): отдельный контекст с
 * файлом `storageState` к этому моменту уже протух бы, потому что браузер
 * ротировал refresh-cookie. `404` и `409` терпим — строку могли отменить раньше.
 */
export async function cancelBookingsByGuestName(
  request: APIRequestContext,
  fragment: string
): Promise<void> {
  const token = await csrfToken(request)
  const list = await request.get(
    '/api/barber/bookings?scope=upcoming&pageSize=20'
  )

  expect(list.status(), 'GET /api/barber/bookings').toBe(200)

  const body = (await list.json()) as {
    items: { id: string; customer: { displayName: string } }[]
  }

  for (const booking of body.items) {
    if (!booking.customer.displayName.includes(fragment)) {
      continue
    }

    const response = await request.post(
      `/api/bookings/${booking.id}/cancellation`,
      {
        headers: { 'X-CSRF-TOKEN': token },
        data: { reason: 'E2E: уборка фикстур переноса записей' },
      }
    )

    expect(
      [204, 404, 409],
      `POST /api/bookings/{id}/cancellation: ${response.status()}`
    ).toContain(response.status())
  }
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
