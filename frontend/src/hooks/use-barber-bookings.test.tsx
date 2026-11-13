import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useBarberBookings } from '@/hooks/use-barber-bookings'
import type { BarberBooking } from '@/lib/barber-bookings'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Тихое обновление возвращает разницу, по которой экран строит баннер.
 *
 * «Новая» — только строка, которой барбер ещё не видел: сдвиг страницы не должен
 * объявляться новой записью.
 */

const booking = (
  id: string,
  status: BarberBooking['status'] = 'Pending'
): BarberBooking => ({
  id,
  status,
  startAt: '2026-11-10T09:00:00Z',
  endAt: '2026-11-10T09:30:00Z',
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
})

describe('useBarberBookings: тихое обновление', () => {
  it('возвращает новую запись и не считает новой строку соседней страницы', async () => {
    let items = [booking('a'), booking('b')]

    server.use(
      http.get('/api/barber/bookings', () =>
        HttpResponse.json({ items, nextCursor: null })
      )
    )

    const { result } = renderHook(() => useBarberBookings('upcoming'))

    await waitFor(() => {
      expect(result.current.items).toHaveLength(2)
    })

    // Сервер вернул ту же страницу с новой записью впереди — и без строки «b».
    items = [booking('new'), booking('a')]

    const change = await act(async () => result.current.refresh())

    expect(change?.added.map((item) => item.id)).toStrictEqual(['new'])
    expect(change?.removed.map((item) => item.id)).toStrictEqual(['b'])

    await waitFor(() => {
      expect(result.current.items.map((item) => item.id)).toStrictEqual([
        'new',
        'a',
      ])
    })
  })

  it('постоянный список не даёт изменений', async () => {
    const items = [booking('a', 'Confirmed')]

    server.use(
      http.get('/api/barber/bookings', () =>
        HttpResponse.json({ items, nextCursor: null })
      )
    )

    const { result } = renderHook(() => useBarberBookings('upcoming'))

    await waitFor(() => {
      expect(result.current.items).toHaveLength(1)
    })

    const change = await act(async () => result.current.refresh())

    expect(change).toStrictEqual({ added: [], removed: [], updated: [] })
  })

  it('перенос обновляет строку на месте, без повторного GET', async () => {
    const startAt = '2026-11-12T14:00:00Z'
    const endAt = '2026-11-12T14:30:00Z'
    const fetches = countRequests('/api/barber/bookings')
    const posts: string[] = []

    server.use(
      http.get('/api/barber/bookings', () =>
        HttpResponse.json({ items: [booking('a')], nextCursor: null })
      ),
      http.post('/api/bookings/:id/reschedule', async ({ request }) => {
        posts.push(await request.text())

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useBarberBookings('upcoming'))

    await waitFor(() => {
      expect(result.current.items).toHaveLength(1)
    })

    const fetchesBefore = fetches()

    await act(async () => {
      await result.current.reschedule('a', { startAt, endAt })
    })

    expect(posts).toStrictEqual([JSON.stringify({ startAt })])
    expect(result.current.items[0]).toMatchObject({ id: 'a', startAt, endAt })
    expect(fetches()).toBe(fetchesBefore)
    expect(result.current.isMutating).toBe(false)
  })

  it('второй клик по той же строке не шлёт второй POST (single-flight)', async () => {
    let posts = 0

    server.use(
      http.get('/api/barber/bookings', () =>
        HttpResponse.json({ items: [booking('a')], nextCursor: null })
      ),
      http.post('/api/bookings/:id/reschedule', () => {
        posts += 1

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useBarberBookings('upcoming'))

    await waitFor(() => {
      expect(result.current.items).toHaveLength(1)
    })

    await act(async () => {
      await Promise.all([
        result.current.reschedule('a', {
          startAt: '2026-11-12T14:00:00Z',
          endAt: '2026-11-12T14:30:00Z',
        }),
        result.current.reschedule('a', {
          startAt: '2026-11-12T15:00:00Z',
          endAt: '2026-11-12T15:30:00Z',
        }),
      ])
    })

    expect(posts).toBe(1)
  })
})
