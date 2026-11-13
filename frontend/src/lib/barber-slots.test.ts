import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import {
  fetchBarberSlots,
  salonDateOf,
  todayInTimeZone,
  type BarberSlots,
} from '@/lib/barber-slots'
import { server } from '@/test/msw/server'

/**
 * Даты и режимы чтения слотов.
 *
 * `salonDateOf` — календарная дата инстанта в поясе салона: диалог переноса
 * открывается на дне самой брони, а не на дне браузера.
 */

const SLOTS: BarberSlots = {
  barberProfileId: '11111111-1111-4111-8111-111111111111',
  salonId: '22222222-2222-4222-8222-222222222222',
  barberServiceId: '33333333-3333-4333-8333-333333333333',
  hairServiceId: null,
  date: '2026-11-06',
  timeZoneId: 'Asia/Yekaterinburg',
  dayStart: '2026-11-06T00:00:00Z',
  dayEnd: '2026-11-06T19:00:00Z',
  durationMinutes: 30,
  isWorkingDay: true,
  slots: [{ startAt: '2026-11-06T04:00:00Z', endAt: '2026-11-06T04:30:00Z' }],
}

describe('salonDateOf', () => {
  it('берёт календарную дату в поясе салона, а не в UTC', () => {
    const instant = '2026-11-05T21:30:00Z'

    expect(salonDateOf(instant, 'Asia/Yekaterinburg')).toBe('2026-11-06')
    expect(salonDateOf(instant, 'Europe/Berlin')).toBe('2026-11-05')
  })

  it('инстант у полуночи не переезжает на соседний день', () => {
    const midnightInYekaterinburg = '2026-11-05T19:00:00Z'

    expect(
      salonDateOf(midnightInYekaterinburg, 'Asia/Yekaterinburg')
    ).toBe('2026-11-06')
    expect(salonDateOf(midnightInYekaterinburg, 'Europe/Berlin')).toBe(
      '2026-11-05'
    )
  })

  it('битый пояс — фолбэк на пояс браузера', () => {
    const instant = '2026-11-05T12:00:00Z'
    const expected = new Intl.DateTimeFormat('en-CA', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date(instant))

    expect(salonDateOf(instant, 'Not/AZone')).toBe(expected)
  })

  it('битый инстант — пустая дата, запрос слотов не отправится', () => {
    expect(salonDateOf('не дата', 'Europe/Berlin')).toBe('')
  })
})

describe('todayInTimeZone', () => {
  it('без пояса считает в поясе браузера и отдаёт YYYY-MM-DD', () => {
    expect(todayInTimeZone()).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})

describe('fetchBarberSlots: режим брони', () => {
  it('читает /bookings/{id}/reschedule-slots только с датой', async () => {
    const requests: string[] = []

    server.use(
      http.get('/api/bookings/:bookingId/reschedule-slots', ({ params, request }) => {
        requests.push(String(params.bookingId))
        requests.push(new URL(request.url).search)

        return HttpResponse.json(SLOTS)
      })
    )

    await expect(
      fetchBarberSlots({ bookingId: 'booking-1', date: '2026-11-06' })
    ).resolves.toStrictEqual(SLOTS)

    expect(requests).toStrictEqual(['booking-1', '?date=2026-11-06'])
  })

  it('публичный режим не тронут: профиль, услуга и салон на месте', async () => {
    const searches: string[] = []

    server.use(
      http.get('/api/barbers/:id/slots', ({ request }) => {
        searches.push(new URL(request.url).search)

        return HttpResponse.json(SLOTS)
      })
    )

    await fetchBarberSlots({
      barberProfileId: 'profile-1',
      date: '2026-11-06',
      hairServiceId: 'hair-1',
      salonId: 'salon-1',
    })

    expect(searches).toStrictEqual([
      '?date=2026-11-06&hairServiceId=hair-1&salonId=salon-1',
    ])
  })
})
