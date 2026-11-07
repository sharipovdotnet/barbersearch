import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useUnreadNotifications } from '@/hooks/use-unread-notifications'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

// jsdom отдаёт `document.visibilityState`, но в тесте важно, чтобы «возврат на
// вкладку» гарантированно считался видимым, а не зависел от версии jsdom.
Object.defineProperty(document, 'visibilityState', {
  configurable: true,
  get: () => 'visible',
})

describe('useUnreadNotifications', () => {
  it('делает один GET при монтировании и показывает счётчик', async () => {
    let calls = 0

    server.use(
      http.get('/api/barber/notifications/unread-count', () => {
        calls += 1

        return HttpResponse.json({ count: 3 })
      })
    )

    const { result } = renderHook(() => useUnreadNotifications())

    expect(result.current.count).toBeNull()

    await waitFor(() => {
      expect(result.current.count).toBe(3)
    })

    expect(calls).toBe(1)
  })

  it('тихо обновляет счётчик по таймеру', async () => {
    let calls = 0

    server.use(
      http.get('/api/barber/notifications/unread-count', () => {
        calls += 1

        return HttpResponse.json({ count: calls })
      })
    )

    const { result } = renderHook(() =>
      useUnreadNotifications({ intervalMs: 20 })
    )

    // Счётчик растёт только со вторым ответом: первый вернул `count: 1`.
    // Ждём состояние, а не счётчик запросов: `calls` увеличивается в обработчике
    // MSW до того, как ответ применится к React-состоянию, — гонка делала тест
    // плавающим под нагрузкой.
    await waitFor(() => {
      expect(result.current.count).toBeGreaterThanOrEqual(2)
    })

    expect(calls).toBeGreaterThanOrEqual(2)
  })

  it('обновляет счётчик при возврате на вкладку', async () => {
    let count = 3

    server.use(
      http.get('/api/barber/notifications/unread-count', () =>
        HttpResponse.json({ count })
      )
    )

    const { result } = renderHook(() => useUnreadNotifications())

    await waitFor(() => {
      expect(result.current.count).toBe(3)
    })

    count = 5

    act(() => {
      window.dispatchEvent(new Event('focus'))
    })

    await waitFor(() => {
      expect(result.current.count).toBe(5)
    })

    count = 7

    act(() => {
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await waitFor(() => {
      expect(result.current.count).toBe(7)
    })
  })

  it('скрывает бейдж при ошибке и не показывает текст', async () => {
    let calls = 0

    server.use(
      http.get('/api/barber/notifications/unread-count', () => {
        calls += 1

        return HttpResponse.json(problemDetails(500), { status: 500 })
      })
    )

    const { result } = renderHook(() => useUnreadNotifications())

    await waitFor(() => {
      expect(calls).toBe(1)
    })

    expect(result.current.count).toBeNull()
  })
})
