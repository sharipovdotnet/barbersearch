import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  createBarberBooking,
  type CreateBarberBookingInput,
} from '@/lib/barber-booking-create'
import { describeBarberBookingCreateFailure } from '@/lib/errors'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const INPUT: CreateBarberBookingInput = {
  customerId: '22222222-2222-4222-8222-222222222222',
  barberServiceId: '33333333-3333-4333-8333-333333333333',
  salonId: '44444444-4444-4444-8444-444444444444',
  startAt: '2026-11-05T07:00:00Z',
  endAt: '2026-11-05T07:30:00Z',
  idempotencyKey: 'client-booking-key',
  customerNote: null,
}

describe('createBarberBooking', () => {
  it('шлёт cookie-мутацию с CSRF-заголовком и возвращает id', async () => {
    const requests: { csrf: string | null; body: string }[] = []

    server.use(
      http.post('/api/barber/bookings', async ({ request }) => {
        requests.push({
          csrf: request.headers.get('x-csrf-token'),
          body: await request.text(),
        })

        return HttpResponse.json(
          { id: '55555555-5555-4555-8555-555555555555' },
          { status: 201 }
        )
      })
    )

    const id = await createBarberBooking(INPUT)

    expect(id).toBe('55555555-5555-4555-8555-555555555555')
    expect(requests).toHaveLength(1)
    expect(requests[0].csrf).toBe('csrf-token')
    expect(JSON.parse(requests[0].body)).toStrictEqual(INPUT)
  })

  it('повторяет запрос один раз на устаревший CSRF-токен', async () => {
    let posts = 0

    server.use(
      http.post('/api/barber/bookings', async () => {
        posts += 1

        if (posts === 1) {
          return HttpResponse.json(
            problemDetails(400, {
              errorCodes: ['auth.csrf.invalid'],
            }),
            { status: 400 }
          )
        }

        return HttpResponse.json(
          { id: '55555555-5555-4555-8555-555555555555' },
          { status: 201 }
        )
      })
    )

    await createBarberBooking(INPUT)

    expect(posts).toBe(2)
  })

  it('не повторяет конфликт времени', async () => {
    let posts = 0

    server.use(
      http.post('/api/barber/bookings', () => {
        posts += 1

        return HttpResponse.json(
          problemDetails(409, { errorCodes: ['booking.add.overlap'] }),
          { status: 409 }
        )
      })
    )

    await expect(createBarberBooking(INPUT)).rejects.toBeInstanceOf(ApiError)
    expect(posts).toBe(1)
  })
})

describe('describeBarberBookingCreateFailure', () => {
  function failure(
    status: number,
    codes: string[]
  ): ReturnType<typeof describeBarberBookingCreateFailure> {
    return describeBarberBookingCreateFailure(
      new ApiError(
        status,
        problemDetails(status, { errorCodes: codes }),
        'Server error.'
      ),
      'Не удалось записать клиента.'
    )
  }

  it('объясняет, что клиент исчез, а не «страница не найдена»', () => {
    expect(failure(404, ['booking.create.customerNotFound'])).toStrictEqual({
      message:
        'Клиент не найден — возможно, аккаунт заблокирован. Повторите поиск.',
      reloadSlots: false,
    })
  })

  it('на пересечение времени просит перезапросить слоты', () => {
    expect(failure(409, ['booking.add.overlap'])).toStrictEqual({
      message: 'Это время только что заняли. Выберите другое.',
      reloadSlots: true,
    })
  })

  it('на прошедшее время просит перезапросить слоты', () => {
    expect(failure(400, ['booking.create.startAt.inPast'])).toStrictEqual({
      message: 'Это время уже недоступно. Обновите свободное время.',
      reloadSlots: true,
    })
  })

  it('на чужую роль объясняет доступ без detail', () => {
    expect(failure(403, ['auth.forbidden'])).toStrictEqual({
      message: 'Нет доступа к записи клиентов. Войдите под аккаунтом барбера.',
      reloadSlots: false,
    })
  })

  it('на таймаут блокировки оставляет выбор и предлагает повторить', () => {
    expect(failure(409, ['booking.add.lockTimeout'])).toStrictEqual({
      message: 'Не удалось занять время, попробуйте ещё раз.',
      reloadSlots: false,
    })
  })
})
