import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  ADMIN_BOOKING_STATUS_FILTER_LABELS,
  ADMIN_BOOKINGS_PAGE_SIZE,
  ADMIN_BOOKINGS_SEARCH_MAX_LENGTH,
  cancelAdminBooking,
  describeAdminBookingActionError,
  describeAdminBookingsError,
  fetchAdminBooking,
  fetchAdminBookings,
  formatAdminBookingTime,
  markAdminBookingNoShow,
} from '@/lib/admin-bookings'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Транспорт, guard'ы и ошибки админских броней.
 *
 * Контракт списка: `status` уходит всегда (`all` по умолчанию), пустой `search`
 * и отсутствующий `cursor` — нет, `pageSize` в URL не добавляется. Фильтры живут
 * в состоянии React, поэтому email клиента попадает только в параметр запроса,
 * а не в адрес страницы.
 */

const BOOKINGS_URL = '/api/admin/bookings'
const BOOKING_URL = `${BOOKINGS_URL}/9e900000-0000-4000-8000-000000000001`
const BOOKING_ID = '9e900000-0000-4000-8000-000000000001'
const CUSTOMER_AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000009/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

/** Полная строка ответа: ровно то, что читает экран. */
const BOOKING = {
  id: BOOKING_ID,
  status: 'Pending',
  startAt: '2026-09-12T09:00:00+00:00',
  endAt: '2026-09-12T09:30:00+00:00',
  price: 30,
  customerNote: 'Позвоните за час',
  barberNote: 'Клиент предупреждён',
  cancellationReason: null,
  createdAt: '2026-09-11T12:00:00+00:00',
  customer: {
    id: '9e000000-0000-4000-8000-000000000009',
    firstName: 'Ирина',
    lastName: 'Волкова',
    email: 'e2e-bookings@barbersearch.test',
    phoneNumber: '+70000000009',
    avatarUrl: CUSTOMER_AVATAR_URL,
  },
  barber: {
    profileId: '9e100000-0000-4000-8000-000000000001',
    displayName: 'Артём Соколов',
    avatarUrl: null,
  },
  service: { name: 'Стрижка' },
  salon: {
    id: '9e400000-0000-4000-8000-000000000001',
    name: 'BarberSearch Тверская',
    address: 'ул. Тверская, 12',
    city: 'Москва',
    timeZoneId: 'Europe/Moscow',
  },
}

/** Перехватывает GET списка и складывает параметры каждого запроса. */
function captureBookings(): URLSearchParams[] {
  const captured: URLSearchParams[] = []

  server.use(
    http.get(BOOKINGS_URL, ({ request }) => {
      captured.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items: [], nextCursor: null })
    })
  )

  return captured
}

function entries(params: URLSearchParams): [string, string][] {
  return [...params.entries()]
}

describe('fetchAdminBookings', () => {
  it('по умолчанию отправляет только status=all', async () => {
    const captured = captureBookings()

    await expect(fetchAdminBookings({ status: 'all' })).resolves.toStrictEqual({
      items: [],
      nextCursor: null,
    })

    expect(captured).toHaveLength(1)
    expect(entries(captured[0])).toStrictEqual([['status', 'all']])
  })

  it('без явного статуса тоже отправляет status=all', async () => {
    const captured = captureBookings()

    await fetchAdminBookings({})

    expect(entries(captured[0])).toStrictEqual([['status', 'all']])
  })

  it('передаёт статус и поиск в порядке URLSearchParams', async () => {
    const captured = captureBookings()

    await fetchAdminBookings({ status: 'Pending', search: 'e2e-bookings' })

    expect(entries(captured[0])).toStrictEqual([
      ['status', 'Pending'],
      ['search', 'e2e-bookings'],
    ])
  })

  it('не отправляет пустой поиск', async () => {
    const captured = captureBookings()

    await fetchAdminBookings({ status: 'all', search: '   ' })

    expect(entries(captured[0])).toStrictEqual([['status', 'all']])
  })

  it('добавляет курсор последним и не добавляет pageSize', async () => {
    const captured = captureBookings()

    await fetchAdminBookings({
      status: 'NoShow',
      search: 'e2e-bookings',
      cursor: 'opaque-cursor',
    })

    expect(entries(captured[0])).toStrictEqual([
      ['status', 'NoShow'],
      ['search', 'e2e-bookings'],
      ['cursor', 'opaque-cursor'],
    ])
    expect(captured[0].has('pageSize')).toBe(false)
  })

  it('битый ответ списка — ошибка', async () => {
    server.use(
      http.get(BOOKINGS_URL, () =>
        HttpResponse.json({ items: [{ id: '' }], nextCursor: null })
      )
    )

    await expect(fetchAdminBookings({ status: 'all' })).rejects.toThrow(ApiError)
  })

  it('лишние поля ответа не ломают guard', async () => {
    server.use(
      http.get(BOOKINGS_URL, () =>
        HttpResponse.json({
          items: [
            {
              ...BOOKING,
              databaseRowId: 42,
              customer: { ...BOOKING.customer, loyaltyPoints: 12 },
              salon: { ...BOOKING.salon, latitude: 55.76 },
            },
          ],
          nextCursor: 'next',
          totalCount: 24,
        })
      )
    )

    const page = await fetchAdminBookings({ status: 'all' })

    expect(page.nextCursor).toBe('next')
    expect(page.items).toHaveLength(1)
    expect(page.items[0].id).toBe(BOOKING_ID)
    expect(page.items[0].customer.email).toBe('e2e-bookings@barbersearch.test')
    expect(page.items[0].salon.timeZoneId).toBe('Europe/Moscow')
  })

  it('guard строгий: неверный статус — битый ответ', async () => {
    server.use(
      http.get(BOOKINGS_URL, () =>
        HttpResponse.json({
          items: [{ ...BOOKING, status: 'Archived' }],
          nextCursor: null,
        })
      )
    )

    await expect(fetchAdminBookings({ status: 'all' })).rejects.toThrow(ApiError)
  })

  it('guard строгий: цена строкой — битый ответ', async () => {
    server.use(
      http.get(BOOKINGS_URL, () =>
        HttpResponse.json({
          items: [{ ...BOOKING, price: '30' }],
          nextCursor: null,
        })
      )
    )

    await expect(fetchAdminBookings({ status: 'all' })).rejects.toThrow(ApiError)
  })

  it('guard строгий: без часового пояса салона — битый ответ', async () => {
    server.use(
      http.get(BOOKINGS_URL, () =>
        HttpResponse.json({
          items: [
            {
              ...BOOKING,
              salon: { ...BOOKING.salon, timeZoneId: undefined },
            },
          ],
          nextCursor: null,
        })
      )
    )

    await expect(fetchAdminBookings({ status: 'all' })).rejects.toThrow(ApiError)
  })
})

describe('fetchAdminBooking', () => {
  it('читает карточку по id', async () => {
    server.use(http.get(BOOKING_URL, () => HttpResponse.json(BOOKING)))

    const booking = await fetchAdminBooking(BOOKING_ID)

    expect(booking.id).toBe(BOOKING_ID)
    expect(booking.customer.firstName).toBe('Ирина')
    expect(booking.customer.avatarUrl).toBe(CUSTOMER_AVATAR_URL)
    expect(booking.barber.displayName).toBe('Артём Соколов')
    expect(booking.barber.avatarUrl).toBeNull()
    expect(booking.service.name).toBe('Стрижка')
    expect(booking.salon.city).toBe('Москва')
  })

  it('битый ответ карточки — ошибка', async () => {
    server.use(
      http.get(BOOKING_URL, () =>
        HttpResponse.json({ ...BOOKING, customer: null })
      )
    )

    await expect(fetchAdminBooking(BOOKING_ID)).rejects.toThrow(ApiError)
  })

  it('пробрасывает 404 карточки как ApiError', async () => {
    server.use(
      http.get(BOOKING_URL, () =>
        HttpResponse.json(
          problemDetails(404, { errorCodes: ['admin.bookings.notFound'] }),
          { status: 404 }
        )
      )
    )

    await expect(fetchAdminBooking(BOOKING_ID)).rejects.toThrow(ApiError)
  })
})

describe('мутации брони', () => {
  it('отмена идёт POST на cancellation с CSRF-заголовком', async () => {
    const requests: { method: string; path: string; csrf: string | null }[] = []

    server.use(
      http.post('/api/bookings/:bookingId/cancellation', ({ request }) => {
        requests.push({
          method: request.method,
          path: new URL(request.url).pathname,
          csrf: request.headers.get('x-csrf-token'),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    await cancelAdminBooking(BOOKING_ID)

    expect(requests).toStrictEqual([
      {
        method: 'POST',
        path: `/api/bookings/${BOOKING_ID}/cancellation`,
        csrf: 'csrf-token',
      },
    ])
  })

  it('неявка идёт POST на no-show с CSRF-заголовком', async () => {
    const requests: { method: string; path: string; csrf: string | null }[] = []

    server.use(
      http.post('/api/bookings/:bookingId/no-show', ({ request }) => {
        requests.push({
          method: request.method,
          path: new URL(request.url).pathname,
          csrf: request.headers.get('x-csrf-token'),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    await markAdminBookingNoShow(BOOKING_ID)

    expect(requests).toStrictEqual([
      {
        method: 'POST',
        path: `/api/bookings/${BOOKING_ID}/no-show`,
        csrf: 'csrf-token',
      },
    ])
  })
})

describe('formatAdminBookingTime', () => {
  it('показывает время в поясе салона, а не браузера', () => {
    // 20:30 UTC — это 23:30 в Москве; в поясе браузера теста было бы другое время.
    const value = formatAdminBookingTime({
      startAt: '2026-09-12T20:30:00+00:00',
      salon: { ...BOOKING.salon, timeZoneId: 'Europe/Moscow' },
    })

    expect(value).toContain('23:30')
    expect(value).not.toContain('01:30')
  })
})

describe('подписи и константы контракта', () => {
  it('подписи фильтра статуса — русские', () => {
    expect(ADMIN_BOOKING_STATUS_FILTER_LABELS).toStrictEqual({
      all: 'Все статусы',
      Pending: 'Ждёт подтверждения',
      Confirmed: 'Подтверждена',
      Completed: 'Завершена',
      Cancelled: 'Отменена',
      NoShow: 'Неявка',
    })
  })

  it('размер страницы и лимит поиска совпадают с сервером', () => {
    expect(ADMIN_BOOKINGS_PAGE_SIZE).toBe(20)
    expect(ADMIN_BOOKINGS_SEARCH_MAX_LENGTH).toBe(100)
  })
})

describe('describeAdminBookingsError', () => {
  it('403 объясняет роль', () => {
    const error = new ApiError(403, problemDetails(403), 'Forbidden.')

    expect(describeAdminBookingsError(error, 'Не удалось загрузить брони.')).toBe(
      'Раздел доступен только администратору.'
    )
  })

  it('устаревший курсор предлагает начать с первой страницы', () => {
    const error = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['admin.bookings.cursor.invalid'] }),
      'Invalid.'
    )

    expect(describeAdminBookingsError(error, 'Не удалось загрузить брони.')).toBe(
      'Список устарел. Обновите и начните с первой страницы.'
    )
  })

  it('429 просит подождать минуту', () => {
    const error = new ApiError(429, problemDetails(429), 'Too many.')

    expect(describeAdminBookingsError(error, 'Не удалось загрузить брони.')).toBe(
      'Слишком много запросов. Подождите минуту и повторите.'
    )
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(
      describeAdminBookingsError(new Error('сеть'), 'Не удалось загрузить брони.')
    ).toBe('Не удалось загрузить брони.')
  })
})

describe('describeAdminBookingActionError', () => {
  const fallback = 'Не удалось выполнить действие.'

  it('отмена уже завершённой брони объясняет статус', () => {
    const error = new ApiError(
      409,
      problemDetails(409, { errorCodes: ['booking.cancel.invalidStatus'] }),
      'Invalid status.'
    )

    expect(describeAdminBookingActionError(error, fallback)).toBe(
      'Бронь уже завершена или отменена.'
    )
  })

  it('неявка до начала визита объясняет время', () => {
    const error = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['booking.markNoShow.tooEarly'] }),
      'Too early.'
    )

    expect(describeAdminBookingActionError(error, fallback)).toBe(
      'Неявку можно отметить только после начала визита.'
    )
  })

  it('неявка для неподтверждённой брони объясняет статус', () => {
    const error = new ApiError(
      409,
      problemDetails(409, { errorCodes: ['booking.markNoShow.invalidStatus'] }),
      'Invalid status.'
    )

    expect(describeAdminBookingActionError(error, fallback)).toBe(
      'Неявку отмечают только для подтверждённой брони.'
    )
  })

  it('пропавшая бронь объясняется без английского detail', () => {
    const error = new ApiError(
      404,
      problemDetails(404, {
        detail: 'Booking was not found.',
        errorCodes: ['admin.bookings.notFound'],
      }),
      'Booking was not found.'
    )

    expect(describeAdminBookingActionError(error, fallback)).toBe(
      'Бронь не найдена.'
    )
  })

  it('403 и 429 берут общие тексты', () => {
    expect(
      describeAdminBookingActionError(
        new ApiError(403, problemDetails(403), 'Forbidden.'),
        fallback
      )
    ).toBe('Раздел доступен только администратору.')

    expect(
      describeAdminBookingActionError(
        new ApiError(429, problemDetails(429), 'Too many.'),
        fallback
      )
    ).toBe('Слишком много запросов. Подождите минуту и повторите.')
  })

  it('неизвестная ошибка отдаёт запасной текст', () => {
    expect(describeAdminBookingActionError(new Error('сеть'), fallback)).toBe(
      fallback
    )
  })
})
