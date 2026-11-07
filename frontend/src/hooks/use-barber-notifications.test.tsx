import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useBarberNotifications } from '@/hooks/use-barber-notifications'
import { unauthorized } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const UPCOMING_ID = '11111111-1111-4111-8111-111111111111'
const BOOKING_ID = '22222222-2222-4222-8222-222222222222'
const DIGEST_ID = '33333333-3333-4333-8333-333333333333'

function upcoming(readAt: string | null = null) {
  return {
    id: UPCOMING_ID,
    kind: 'Upcoming',
    createdAt: '2026-11-07T09:02:00Z',
    readAt,
    bookingId: BOOKING_ID,
    startAt: '2026-11-07T09:00:00Z',
    serviceName: 'стрижка',
    salonName: 'Барбершоп на Ленина',
    salonTimeZoneId: 'Asia/Yekaterinburg',
    bookingsCount: null,
    delivery: 'Sent',
  }
}

function digest(readAt: string | null = null) {
  return {
    id: DIGEST_ID,
    kind: 'Digest',
    createdAt: '2026-11-07T03:00:00Z',
    readAt,
    bookingId: null,
    startAt: '2026-11-07T05:00:00Z',
    serviceName: null,
    salonName: null,
    salonTimeZoneId: 'Asia/Yekaterinburg',
    bookingsCount: 4,
    delivery: 'Skipped',
  }
}

describe('useBarberNotifications', () => {
  it('листает страницы по nextCursor: «Назад» не делает запрос', async () => {
    const cursors: (string | null)[] = []

    server.use(
      http.get('/api/barber/notifications', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')
        cursors.push(cursor)

        return HttpResponse.json(
          cursor === null
            ? { items: [upcoming(), digest()], nextCursor: 'cursor-2' }
            : { items: [digest('2026-11-07T10:00:00Z')], nextCursor: null }
        )
      })
    )

    const { result } = renderHook(() => useBarberNotifications())

    await waitFor(() => {
      expect(result.current.items).toHaveLength(2)
    })

    await act(async () => {
      result.current.goNext()
    })

    await waitFor(() => {
      expect(result.current.items).toHaveLength(1)
    })

    const before = cursors.length

    await act(async () => {
      result.current.goPrev()
    })

    expect(result.current.items).toHaveLength(2)
    expect(cursors.length, 'страница из кэша').toBe(before)
  })

  it('отмечает строку прочитанной и убирает её из непрочитанных', async () => {
    const reads: string[] = []

    server.use(
      http.get('/api/barber/notifications', () =>
        HttpResponse.json({ items: [upcoming(), digest()], nextCursor: null })
      ),
      http.post('/api/barber/notifications/:id/read', ({ request }) => {
        reads.push(new URL(request.url).pathname)

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useBarberNotifications())

    await waitFor(() => {
      expect(result.current.items).toHaveLength(2)
    })

    expect(result.current.unreadCount).toBe(2)

    await act(async () => {
      await result.current.read(UPCOMING_ID)
    })

    expect(reads).toStrictEqual([
      `/api/barber/notifications/${UPCOMING_ID}/read`,
    ])
    expect(result.current.items[0]?.readAt).not.toBeNull()
    expect(result.current.unreadCount).toBe(1)

    // Повторный клик по прочитанной строке запрос не шлёт.
    await act(async () => {
      await result.current.read(UPCOMING_ID)
    })

    expect(reads).toHaveLength(1)
  })

  it('«Прочитать все» шлёт read-all и тихо перечитывает страницу', async () => {
    const readAll: string[] = []
    let listCalls = 0

    server.use(
      http.get('/api/barber/notifications', () => {
        listCalls += 1

        return HttpResponse.json({
          items:
            listCalls === 1
              ? [upcoming(), digest()]
              : [
                  upcoming('2026-11-07T10:00:00Z'),
                  digest('2026-11-07T10:00:00Z'),
                ],
          nextCursor: null,
        })
      }),
      http.post('/api/barber/notifications/read-all', ({ request }) => {
        readAll.push(new URL(request.url).pathname)

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useBarberNotifications())

    await waitFor(() => {
      expect(result.current.items).toHaveLength(2)
    })

    let done = false

    await act(async () => {
      done = await result.current.markAllRead()
    })

    expect(done).toBe(true)
    expect(readAll).toStrictEqual(['/api/barber/notifications/read-all'])
    expect(listCalls).toBe(2)
    expect(result.current.items.every((item) => item.readAt !== null)).toBe(true)
    expect(result.current.unreadCount).toBe(0)
    expect(result.current.markAllError).toBeNull()
  })

  it('401 на списке поднимает гейт входа', async () => {
    server.use(
      http.get('/api/barber/notifications', () => unauthorized())
    )

    const { result } = renderHook(() => useBarberNotifications())

    await waitFor(() => {
      expect(result.current.requiresSignIn).toBe(true)
    })

    expect(result.current.status).toBe('error')
    expect(result.current.items).toStrictEqual([])
  })
})
