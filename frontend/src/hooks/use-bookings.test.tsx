import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useCustomerBookings } from '@/hooks/use-bookings'
import type { Booking, BookingStatus } from '@/lib/bookings'
import { server } from '@/test/msw/server'

const USER_ID = '11111111-1111-4111-8111-111111111111'

/** Изменения «Предстоящих» после тихого обновления: подтверждение и отмена. */

const booking = (
  id: string,
  status: BookingStatus = 'Pending'
): Booking => ({
  id,
  userId: USER_ID,
  barberProfileId: '22222222-2222-4222-8222-222222222222',
  barberServiceId: '33333333-3333-4333-8333-333333333333',
  salonId: '44444444-4444-4444-8444-444444444444',
  startAt: '2026-11-10T09:00:00Z',
  endAt: '2026-11-10T09:30:00Z',
  price: 2000,
  status,
  idempotencyKey: `key-${id}`,
  customerNote: null,
  barberNote: null,
  cancellationReason: null,
  hasReview: false,
  createdAt: '2026-11-01T09:00:00Z',
})

/** Обе вкладки читают `/api/bookings`, но с разным `scope`. */
function serveUpcoming(read: () => Booking[]): void {
  server.use(
    http.get('/api/bookings', ({ request }) => {
      const scope = new URL(request.url).searchParams.get('scope')

      return HttpResponse.json({
        items: scope === 'upcoming' ? read() : [],
        nextCursor: null,
      })
    })
  )
}

describe('useCustomerBookings: тихое обновление', () => {
  it('возвращает подтверждение записи', async () => {
    let items = [booking('a')]
    serveUpcoming(() => items)

    const { result } = renderHook(() =>
      useCustomerBookings({ historyEnabled: false, userId: USER_ID })
    )

    await waitFor(() => {
      expect(result.current.upcoming.items).toHaveLength(1)
    })

    items = [booking('a', 'Confirmed')]

    const change = await act(async () => result.current.refreshUpcoming())

    expect(change?.updated).toHaveLength(1)
    expect(change?.updated[0]?.before.status).toBe('Pending')
    expect(change?.updated[0]?.after.status).toBe('Confirmed')
  })

  it('возвращает исчезнувшую запись после отмены барбером', async () => {
    let items = [booking('a'), booking('b')]
    serveUpcoming(() => items)

    const { result } = renderHook(() =>
      useCustomerBookings({ historyEnabled: false, userId: USER_ID })
    )

    await waitFor(() => {
      expect(result.current.upcoming.items).toHaveLength(2)
    })

    items = [booking('b')]

    const change = await act(async () => result.current.refreshUpcoming())

    expect(change?.removed.map((item) => item.id)).toStrictEqual(['a'])
  })

  it('сужает оба списка до вошедшего пользователя', async () => {
    const requests: URL[] = []

    server.use(
      http.get('/api/bookings', ({ request }) => {
        requests.push(new URL(request.url))

        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    renderHook(() =>
      useCustomerBookings({ historyEnabled: true, userId: USER_ID })
    )

    await waitFor(() => {
      const scopes = requests.map((url) => url.searchParams.get('scope'))

      expect(scopes).toContain('upcoming')
      expect(scopes).toContain('past')
    })

    expect(
      requests.filter((url) => url.searchParams.get('userId') === USER_ID)
    ).toHaveLength(requests.length)
  })
})
