import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import { cancelBooking, fetchBarberBookings } from '@/lib/barber-bookings'
import { describeBarberBookingFailure } from '@/lib/errors'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const BOOKING_ID = '77777777-7777-4777-8777-777777777777'
const CUSTOMER_AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000009/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

/** Строка кабинета барбера: клиент с фото и гость без аккаунта. */
function bookingRow(id: string, guest: boolean) {
  return {
    id,
    status: 'Confirmed',
    startAt: '2026-09-12T09:00:00+00:00',
    endAt: '2026-09-12T09:30:00+00:00',
    price: 1500,
    customerNote: null,
    barberNote: null,
    cancellationReason: null,
    customer: {
      displayName: guest ? 'Пётр Гостев' : 'Марк Правкин',
      phoneNumber: '+79990000000',
      avatarUrl: guest ? null : CUSTOMER_AVATAR_URL,
    },
    service: { name: 'Стрижка' },
    salon: {
      name: 'Барбершоп на Ленина',
      address: 'ул. Ленина, 1',
      city: 'Екатеринбург',
      timeZoneId: 'Asia/Yekaterinburg',
    },
  }
}

describe('fetchBarberBookings, аватар клиента', () => {
  it('читает фото зарегистрированного клиента и null у гостя', async () => {
    const captured: URLSearchParams[] = []

    server.use(
      http.get('/api/barber/bookings', ({ request }) => {
        captured.push(new URL(request.url).searchParams)

        return HttpResponse.json({
          items: [
            bookingRow(BOOKING_ID, false),
            bookingRow('88888888-8888-4888-8888-888888888888', true),
          ],
          nextCursor: null,
        })
      })
    )

    const page = await fetchBarberBookings('upcoming')

    expect(captured).toHaveLength(1)
    expect(captured[0].get('scope')).toBe('upcoming')
    expect(captured[0].get('pageSize')).toBe('20')
    expect(page.items[0].customer.avatarUrl).toBe(CUSTOMER_AVATAR_URL)
    expect(page.items[1].customer.avatarUrl).toBeNull()
  })
})

describe('cancelBooking (барбер)', () => {
  it('шлёт причину в теле cookie-мутации с CSRF-заголовком', async () => {
    const requests: { csrf: string | null; body: string }[] = []

    server.use(
      http.post(
        '/api/bookings/:bookingId/cancellation',
        async ({ request }) => {
          requests.push({
            csrf: request.headers.get('x-csrf-token'),
            body: await request.text(),
          })

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    await cancelBooking(BOOKING_ID, '  Барбер заболел  ')

    expect(requests).toStrictEqual([
      {
        csrf: 'csrf-token',
        body: JSON.stringify({ reason: 'Барбер заболел' }),
      },
    ])
  })
})

describe('describeBarberBookingFailure, причина отмены', () => {
  it('объясняет обязательность причины русским текстом', () => {
    const error = new ApiError(
      400,
      problemDetails(400, {
        errorCodes: ['booking.cancel.reasonRequired'],
      }),
      'Validation.'
    )

    expect(
      describeBarberBookingFailure(error, 'Не удалось отменить запись.')
    ).toStrictEqual({
      message: 'Укажите причину отмены.',
      canReload: false,
    })
  })

  it('объясняет слишком длинную причину', () => {
    const error = new ApiError(
      400,
      problemDetails(400, {
        errorCodes: ['booking.cancel.reasonTooLong'],
      }),
      'Validation.'
    )

    expect(
      describeBarberBookingFailure(error, 'Не удалось отменить запись.')
    ).toStrictEqual({
      message: 'Слишком длинная причина — максимум 500 символов.',
      canReload: false,
    })
  })
})
