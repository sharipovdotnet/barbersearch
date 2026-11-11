import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useBarberBookings } from '@/hooks/use-barber-bookings'
import type { BarberBooking } from '@/lib/barber-bookings'
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
})
