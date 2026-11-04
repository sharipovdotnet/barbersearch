import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import { cancelBooking } from '@/lib/barber-bookings'
import { describeBarberBookingFailure } from '@/lib/errors'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const BOOKING_ID = '77777777-7777-4777-8777-777777777777'

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
