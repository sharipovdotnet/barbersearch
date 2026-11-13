import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useBarberBookings } from '@/hooks/use-barber-bookings'
import { PARTIAL_ERROR, useBarberDay } from '@/hooks/use-barber-day'
import type { BarberBooking } from '@/lib/barber-bookings'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Лента дня поверх двух существующих контроллеров.
 *
 * Часы заморожены на полудне: «сегодня» в тесте не должно зависеть от того, когда
 * его запустили, иначе строка в 23:59 иногда попадала бы на соседний день.
 */

const NOW = new Date(2026, 10, 8, 12, 0, 0, 0)

function at(hours: number, minutes: number, dayOffset = 0): string {
  const date = new Date(2026, 10, 8, hours, minutes, 0, 0)
  date.setDate(date.getDate() + dayOffset)

  return date.toISOString()
}

function booking(
  id: string,
  startAt: string,
  endAt: string,
  status: BarberBooking['status'] = 'Confirmed'
): BarberBooking {
  return {
    id,
    status,
    startAt,
    endAt,
    price: 2000,
    customerNote: null,
    barberNote: null,
    cancellationReason: null,
    customer: { displayName: 'Анна Петрова', phoneNumber: null, avatarUrl: null },
    service: { name: 'Стрижка' },
    salon: {
      name: 'Салон на Ленина',
      address: 'ул. Ленина, 1',
      city: 'Екатеринбург',
      timeZoneId: 'Asia/Yekaterinburg',
    },
  }
}

type Lists = { upcoming: BarberBooking[]; past: BarberBooking[] }

/**
 * Один обработчик на оба скоупа и журнал запросов: тесту важны и строки, и то,
 * какой список перечитали после действия.
 */
function serveLists(lists: Lists): string[] {
  const calls: string[] = []

  server.use(
    http.get('/api/barber/bookings', ({ request }) => {
      const url = new URL(request.url)
      const scope = url.searchParams.get('scope') === 'past' ? 'past' : 'upcoming'

      calls.push(scope)

      return HttpResponse.json({
        items: lists[scope],
        nextCursor: null,
      })
    })
  )

  return calls
}

function useCabinet() {
  const upcoming = useBarberBookings('upcoming')
  const past = useBarberBookings('past')
  const day = useBarberDay(upcoming, past)

  return { upcoming, past, day }
}

beforeEach(() => {
  vi.useFakeTimers({ now: NOW, toFake: ['Date'] })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useBarberDay: день из двух списков', () => {
  it('на монтировании делает два запроса и собирает день, окно и следующего клиента', async () => {
    const calls = serveLists({
      upcoming: [
        booking('b', at(11, 0), at(11, 30)),
        booking('c', at(12, 0), at(12, 30)),
      ],
      past: [
        booking('a', at(10, 0), at(10, 30), 'Completed'),
        booking('tomorrow', at(10, 0, 1), at(10, 30, 1)),
      ],
    })

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.items).toHaveLength(3)
    })

    expect(calls).toStrictEqual(['upcoming', 'past'])
    expect(result.current.day.items.map((item) => item.id)).toStrictEqual([
      'a',
      'b',
      'c',
    ])
    expect(result.current.day.gaps).toStrictEqual([
      { startAt: at(10, 30), endAt: at(11, 0), minutes: 30 },
      { startAt: at(11, 30), endAt: at(12, 0), minutes: 30 },
    ])
    expect(result.current.day.nextBooking?.id).toBe('c')
    expect(result.current.day.status).toBe('ready')
    expect(result.current.day.isTruncated).toBe(false)
  })

  it('подтверждение уходит в «Ближайшие» и меняет строку локально', async () => {
    let confirmations = 0

    serveLists({
      upcoming: [booking('pending', at(13, 0), at(13, 30), 'Pending')],
      past: [],
    })
    server.use(
      http.post('/api/bookings/pending/confirmation', () => {
        confirmations += 1
        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.items).toHaveLength(1)
    })

    await act(async () => {
      await result.current.day.confirm('pending')
    })

    expect(confirmations).toBe(1)
    expect(result.current.day.items[0].status).toBe('Confirmed')
  })

  it('завершение перечитывает второй список, чтобы строка не пропала из дня', async () => {
    let completions = 0
    const calls = serveLists({
      upcoming: [booking('row', at(10, 0), at(10, 30))],
      past: [],
    })
    server.use(
      http.post('/api/bookings/row/completion', () => {
        completions += 1
        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.items).toHaveLength(1)
    })

    const before = calls.length

    await act(async () => {
      await result.current.day.complete('row')
    })

    expect(completions).toBe(1)
    expect(calls.slice(before)).toStrictEqual(['past'])
  })

  it('перенос уходит в контроллер-владелец, не перечитывая второй список', async () => {
    const calls = serveLists({
      upcoming: [booking('row', at(13, 0), at(13, 30))],
      past: [],
    })
    let posts = 0

    server.use(
      http.post('/api/bookings/row/reschedule', () => {
        posts += 1

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.items).toHaveLength(1)
    })

    const before = calls.length

    await act(async () => {
      await result.current.day.reschedule('row', {
        startAt: at(15, 0),
        endAt: at(15, 30),
      })
    })

    expect(posts).toBe(1)
    expect(result.current.day.items[0]).toMatchObject({
      id: 'row',
      startAt: at(15, 0),
      endAt: at(15, 30),
    })
    // Визит остаётся активным: второй список не перечитывается.
    expect(calls.slice(before)).toStrictEqual([])
  })

  it('401 любого из двух списков — гейт входа', async () => {
    server.use(
      http.get('/api/barber/bookings', ({ request }) => {
        const scope = new URL(request.url).searchParams.get('scope')

        if (scope === 'past') {
          return HttpResponse.json(
            problemDetails(401, { errorCodes: ['auth.required'] }),
            { status: 401 }
          )
        }

        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.requiresSignIn).toBe(true)
    })
  })

  it('ошибка «Истории» при готовых «Ближайших» оставляет ленту и даёт частичную ошибку', async () => {
    server.use(
      http.get('/api/barber/bookings', ({ request }) => {
        const scope = new URL(request.url).searchParams.get('scope')

        if (scope === 'past') {
          return HttpResponse.json(problemDetails(500), { status: 500 })
        }

        return HttpResponse.json({
          items: [booking('row', at(13, 0), at(13, 30))],
          nextCursor: null,
        })
      })
    )

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.items).toHaveLength(1)
    })

    expect(result.current.day.status).toBe('ready')
    expect(result.current.day.error).toBeNull()
    expect(result.current.day.partialError).toBe(PARTIAL_ERROR)
  })

  it('оба списка упали — ошибка дня без строк', async () => {
    server.use(
      http.get('/api/barber/bookings', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    const { result } = renderHook(() => useCabinet())

    await waitFor(() => {
      expect(result.current.day.status).toBe('error')
    })

    expect(result.current.day.items).toStrictEqual([])
    expect(result.current.day.error).toBeTruthy()
    expect(result.current.day.partialError).toBeNull()
  })
})
