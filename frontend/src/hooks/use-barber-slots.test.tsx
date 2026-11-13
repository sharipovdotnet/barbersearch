import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useBarberSlots } from '@/hooks/use-barber-slots'
import type { BarberSlots } from '@/lib/barber-slots'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Режим брони в общем хуке слотов: другой путь, другой текст ошибок, та же
 * механика поколений и отмены.
 */

const BOOKING_ID = '11111111-1111-4111-8111-111111111111'
const PATH = `/api/bookings/${BOOKING_ID}/reschedule-slots`

function slots(date: string): BarberSlots {
  return {
    barberProfileId: '22222222-2222-4222-8222-222222222222',
    salonId: '33333333-3333-4333-8333-333333333333',
    barberServiceId: '44444444-4444-4444-8444-444444444444',
    hairServiceId: null,
    date,
    timeZoneId: 'Europe/Moscow',
    dayStart: `${date}T00:00:00Z`,
    dayEnd: `${date}T21:00:00Z`,
    durationMinutes: 30,
    isWorkingDay: true,
    slots: [
      { startAt: `${date}T07:00:00Z`, endAt: `${date}T07:30:00Z` },
    ],
  }
}

const sleep = (ms: number) =>
  new Promise((resolve) => window.setTimeout(resolve, ms))

describe('useBarberSlots: режим брони', () => {
  it('читает reschedule-slots и меняет запрос при смене даты', async () => {
    const dates: string[] = []

    server.use(
      http.get(PATH, ({ request }) => {
        const date = new URL(request.url).searchParams.get('date') ?? ''

        dates.push(date)

        return HttpResponse.json(slots(date))
      })
    )

    const { result, rerender } = renderHook(
      ({ date }: { date: string }) =>
        useBarberSlots({ bookingId: BOOKING_ID, date }),
      { initialProps: { date: '2026-11-06' } }
    )

    await waitFor(() => {
      expect(result.current.slots?.date).toBe('2026-11-06')
    })

    rerender({ date: '2026-11-07' })

    await waitFor(() => {
      expect(result.current.slots?.date).toBe('2026-11-07')
    })

    expect(dates).toStrictEqual(['2026-11-06', '2026-11-07'])
  })

  it('поздний ответ старой даты не перезаписывает новую', async () => {
    const dates: string[] = []
    // Ворота вместо таймера: первый ответ ждёт, пока тест сам его не отпустит.
    // Так сценарий не зависит от скорости машины — на медленном CI `rerender`
    // успевал отменить запрос раньше, чем он доходил до MSW.
    let releaseOld = () => {}
    const oldGate = new Promise<void>((resolve) => {
      releaseOld = resolve
    })

    server.use(
      http.get(PATH, async ({ request }) => {
        const date = new URL(request.url).searchParams.get('date') ?? ''

        dates.push(date)

        // Первый ответ намеренно висит: он не должен перезаписать второй.
        if (date === '2026-11-06') {
          await oldGate
        }

        return HttpResponse.json(slots(date))
      })
    )

    const { result, rerender } = renderHook(
      ({ date }: { date: string }) =>
        useBarberSlots({ bookingId: BOOKING_ID, date }),
      { initialProps: { date: '2026-11-06' } }
    )

    // Ждём, пока запрос первой даты действительно уйдёт в сеть: без этого
    // `rerender` на быстрой машине отменяет его до MSW, и сценарий перестаёт
    // проверять «поздний ответ».
    await waitFor(() => {
      expect(dates).toContain('2026-11-06')
    })

    rerender({ date: '2026-11-07' })

    await waitFor(() => {
      expect(result.current.slots?.date).toBe('2026-11-07')
    })

    // Отпускаем старый ответ и даём ему прийти: состояние не должно откатиться.
    releaseOld()
    await sleep(50)

    expect(result.current.slots?.date).toBe('2026-11-07')
    expect(dates).toStrictEqual(['2026-11-06', '2026-11-07'])
  })

  it('409 booking.reschedule.status — «перенести уже нельзя»', async () => {
    server.use(
      http.get(PATH, () =>
        HttpResponse.json(
          problemDetails(409, { errorCodes: ['booking.reschedule.status'] }),
          { status: 409 }
        )
      )
    )

    const { result } = renderHook(() =>
      useBarberSlots({ bookingId: BOOKING_ID, date: '2026-11-06' })
    )

    await waitFor(() => {
      expect(result.current.status).toBe('unavailable')
    })

    expect(result.current.error).toBe(
      'Запись уже нельзя перенести — обновите страницу.'
    )
  })

  it('404 — «время или место недоступны», а не публичный текст про салон', async () => {
    server.use(
      http.get(PATH, () =>
        HttpResponse.json(problemDetails(404), { status: 404 })
      )
    )

    const { result } = renderHook(() =>
      useBarberSlots({ bookingId: BOOKING_ID, date: '2026-11-06' })
    )

    await waitFor(() => {
      expect(result.current.status).toBe('unavailable')
    })

    expect(result.current.error).toBe(
      'Запись больше нельзя перенести: время или место недоступны.'
    )
  })

  it('прочий 409 в режиме брони — прежний текст про пояс салона', async () => {
    server.use(
      http.get(PATH, () =>
        HttpResponse.json(
          problemDetails(409, { errorCodes: ['barberSlots.timeZone.invalid'] }),
          { status: 409 }
        )
      )
    )

    const { result } = renderHook(() =>
      useBarberSlots({ bookingId: BOOKING_ID, date: '2026-11-06' })
    )

    await waitFor(() => {
      expect(result.current.status).toBe('unavailable')
    })

    expect(result.current.error).toContain('Расписание салона')
  })
})
