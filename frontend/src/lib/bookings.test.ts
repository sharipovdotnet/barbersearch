import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import { createBooking, fetchBookings, type CreateBookingInput } from '@/lib/bookings'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/** Скоуп — часть контракта списка: сервер фильтрует вкладки, а не клиент. */
describe('fetchBookings', () => {
  it('по умолчанию просит скоуп all и двадцать строк', async () => {
    const requests = captureBookingRequests()

    await expect(fetchBookings()).resolves.toStrictEqual({
      items: [],
      nextCursor: null,
    })

    expect(requests).toStrictEqual(['?scope=all&pageSize=20'])
  })

  it('просит upcoming без курсора на первой странице', async () => {
    const requests = captureBookingRequests()

    await fetchBookings('upcoming', null)

    expect(requests).toStrictEqual(['?scope=upcoming&pageSize=20'])
  })

  it('передаёт скоуп и курсор как есть', async () => {
    const requests = captureBookingRequests()

    await fetchBookings('past', 'cursor-1')

    expect(requests).toStrictEqual([
      '?scope=past&pageSize=20&cursor=cursor-1',
    ])
  })

  it('пустой курсор не попадает в запрос', async () => {
    const requests = captureBookingRequests()

    await fetchBookings('upcoming', '')

    expect(requests).toStrictEqual(['?scope=upcoming&pageSize=20'])
  })

  it('передаёт signal отмены в запрос', async () => {
    server.use(
      http.get('/api/bookings', () => {
        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    const controller = new AbortController()
    controller.abort()

    await expect(
      fetchBookings('all', null, controller.signal)
    ).rejects.toThrow()
  })
})

/** Перехватывает GET-запросы списка и возвращает пустую страницу. */
function captureBookingRequests(): string[] {
  const requests: string[] = []

  server.use(
    http.get('/api/bookings', ({ request }) => {
      requests.push(new URL(request.url).search)

      return HttpResponse.json({ items: [], nextCursor: null })
    })
  )

  return requests
}

/**
 * Создание брони — cookie-мутация, поэтому antiforgery обязателен с этапа #35.
 *
 * Тела запросов проверяются на уровне MSW-обработчика: важно не «примерно тот же URL»,
 * а свежий токен в заголовке, неизменное тело при повторе и отсутствие повтора на `409`.
 */

const input: CreateBookingInput = {
  barberProfileId: '11111111-1111-4111-8111-111111111111',
  barberServiceId: '22222222-2222-4222-8222-222222222222',
  salonId: '33333333-3333-4333-8333-333333333333',
  startAt: '2026-12-01T10:00:00+00:00',
  endAt: '2026-12-01T10:30:00+00:00',
  idempotencyKey: 'booking-key-1',
  customerNote: null,
}

type CapturedBooking = {
  csrfToken: string | null
  body: string
}

async function apiErrorOf(request: Promise<unknown>): Promise<ApiError> {
  try {
    await request
  } catch (error) {
    if (error instanceof ApiError) {
      return error
    }

    throw error
  }

  throw new Error('ожидался ApiError, но запрос завершился успешно')
}

describe('createBooking', () => {
  it('берёт свежий CSRF-токен и шлёт его заголовком с телом брони', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    const bookings: CapturedBooking[] = []

    server.use(
      http.get('/api/auth/csrf', () => HttpResponse.json({ token: 'csrf-token' })),
      http.post('/api/bookings', async ({ request }) => {
        bookings.push({
          csrfToken: request.headers.get('X-CSRF-TOKEN'),
          body: await request.text(),
        })

        return HttpResponse.json({ id: 'booking-1' }, { status: 201 })
      })
    )

    await expect(createBooking(input)).resolves.toBe('booking-1')

    expect(csrfCalls()).toBe(1)
    expect(bookings).toHaveLength(1)
    expect(bookings[0].csrfToken).toBe('csrf-token')
    expect(JSON.parse(bookings[0].body)).toStrictEqual(input)
  })

  it('повторяет один раз на auth.csrf.invalid со свежим токеном и тем же телом', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    const bookings: CapturedBooking[] = []
    let csrfIndex = 0

    server.use(
      http.get('/api/auth/csrf', () => {
        csrfIndex += 1

        return HttpResponse.json({ token: `csrf-${csrfIndex}` })
      }),
      http.post('/api/bookings', async ({ request }) => {
        bookings.push({
          csrfToken: request.headers.get('X-CSRF-TOKEN'),
          body: await request.text(),
        })

        if (bookings.length === 1) {
          return HttpResponse.json(
            problemDetails(400, { errorCodes: ['auth.csrf.invalid'] }),
            { status: 400 }
          )
        }

        return HttpResponse.json({ id: 'booking-1' }, { status: 201 })
      })
    )

    await expect(createBooking(input)).resolves.toBe('booking-1')

    expect(csrfCalls()).toBe(2)
    expect(bookings).toHaveLength(2)
    expect(bookings[0].csrfToken).toBe('csrf-1')
    expect(bookings[1].csrfToken).toBe('csrf-2')
    expect(bookings[1].body).toBe(bookings[0].body)
  })

  it('не повторяет конфликт слота и отдаёт ApiError со статусом 409', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let posts = 0

    server.use(
      http.post('/api/bookings', () => {
        posts += 1

        return HttpResponse.json(
          problemDetails(409, { errorCodes: ['booking.add.overlap'] }),
          { status: 409 }
        )
      })
    )

    const error = await apiErrorOf(createBooking(input))

    expect(error.status).toBe(409)
    expect(error.hasCode('booking.add.overlap')).toBe(true)
    expect(posts).toBe(1)
    expect(csrfCalls()).toBe(1)
  })
})
