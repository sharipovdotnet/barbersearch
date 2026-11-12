import { act, render, within } from '@testing-library/react'
import { http, HttpResponse, type JsonBodyType } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AdminQueuesProvider } from '@/components/admin/admin-queues-provider'
import { AdminRail } from '@/components/admin/admin-rail'
import { AUTO_REFRESH_INTERVAL_MS } from '@/hooks/use-auto-refresh'
import { notifyAdminQueuesChanged } from '@/lib/admin-queues'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Один опрос сводки на всю админку.
 *
 * Проверяем обещания провайдера, а не экрана: ровно один запрос при монтировании,
 * тик раз в 30 секунд, пауза на скрытой вкладке и мгновенное обновление при
 * возврате, событие `admin:queues-changed` после мутации, остановка на `401` и
 * отсутствие бейджей при ошибке. Рейл внутри провайдера — самый заметный
 * потребитель снимка, поэтому наблюдаем через него.
 */

const URL = '/api/admin/summary'

const COUNTS = {
  reviews: { pendingCount: 12, oldest: null },
  barbers: { unverifiedCount: 4 },
  salons: { inactiveCount: 7, pendingModerationCount: 2 },
  photos: { pendingAvatarsCount: 2, pendingPortfolioCount: 3 },
  users: { unverifiedCount: 8 },
  barberRequests: { pendingCount: 3 },
  bookings: { confirmedCount: 140 },
}

function renderQueues() {
  return render(
    <AdminQueuesProvider>
      <AdminRail />
    </AdminQueuesProvider>
  )
}

/** Считает запросы сводки и отвечает одним и тем же телом. */
function mockSummary(body: JsonBodyType): string[] {
  const requests: string[] = []

  server.use(
    http.get(URL, ({ request }) => {
      requests.push(request.url)

      return HttpResponse.json(body)
    })
  )

  return requests
}

function rail(): HTMLElement {
  return document.querySelector('nav') as HTMLElement
}

function badgeOf(label: string): HTMLElement | null {
  const item = within(rail()).getByRole('link', {
    name: new RegExp(`^${label}`),
  })

  return item.querySelector<HTMLElement>('span[aria-hidden="true"]')
}

function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    get: () => state,
  })
  document.dispatchEvent(new Event('visibilitychange'))
}

/** Прокручивает один период опроса, дожидаясь ответа MSW. */
async function tick(ms = AUTO_REFRESH_INTERVAL_MS): Promise<void> {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

afterEach(() => {
  vi.useRealTimers()
  Reflect.deleteProperty(document, 'visibilityState')
})

describe('AdminQueuesProvider', () => {
  it('делает один запрос при монтировании', async () => {
    vi.useFakeTimers()
    const requests = mockSummary(COUNTS)

    renderQueues()
    await tick(0)

    expect(requests).toHaveLength(1)
    expect(badgeOf('Отзывы')?.textContent).toBe('12')

    // До периода нового запроса нет: тик не дублирует первую загрузку.
    await tick(AUTO_REFRESH_INTERVAL_MS - 1)
    expect(requests).toHaveLength(1)
  })

  it('раз в 30 секунд перечитывает сводку', async () => {
    vi.useFakeTimers()
    const requests = mockSummary(COUNTS)

    renderQueues()
    await tick(0)
    expect(requests).toHaveLength(1)

    await tick()
    expect(requests).toHaveLength(2)

    await tick()
    expect(requests).toHaveLength(3)
  })

  it('на скрытой вкладке молчит, а при возврате обновляет сразу', async () => {
    vi.useFakeTimers()
    const requests = mockSummary(COUNTS)

    renderQueues()
    await tick(0)
    expect(requests).toHaveLength(1)

    act(() => {
      setVisibility('hidden')
    })
    await tick(AUTO_REFRESH_INTERVAL_MS * 3)
    expect(requests).toHaveLength(1)

    await act(async () => {
      setVisibility('visible')
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(requests).toHaveLength(2)
  })

  it('событие admin:queues-changed перечитывает сводку сразу', async () => {
    vi.useFakeTimers()
    const requests = mockSummary(COUNTS)

    renderQueues()
    await tick(0)
    expect(requests).toHaveLength(1)

    await act(async () => {
      notifyAdminQueuesChanged()
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(requests).toHaveLength(2)

    // Второе событие подряд — второй тихий refresh, а не ожидание тика.
    await act(async () => {
      notifyAdminQueuesChanged()
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(requests).toHaveLength(3)
  })

  it('401 останавливает опрос и не рисует бейджи', async () => {
    vi.useFakeTimers()
    const requests: string[] = []

    server.use(
      http.get(URL, ({ request }) => {
        requests.push(request.url)

        return HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.required'] }),
          { status: 401 }
        )
      })
    )

    renderQueues()
    await tick(0)

    expect(requests).toHaveLength(1)
    expect(badgeOf('Отзывы')).toBeNull()

    await tick(AUTO_REFRESH_INTERVAL_MS * 3)
    expect(requests).toHaveLength(1)
  })

  it('ошибка не рисует бейджи и не пишет в рейл', async () => {
    vi.useFakeTimers()

    server.use(
      http.get(URL, () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderQueues()
    await tick(0)

    expect(badgeOf('Отзывы')).toBeNull()
    // Рейл не показывает ошибок счётчиков: ни `alert`, ни `status`.
    expect(within(rail()).queryByRole('alert')).toBeNull()
    expect(within(rail()).queryByRole('status')).toBeNull()
  })
})
