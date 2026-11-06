import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { CatalogueScreen } from '@/components/admin/catalogue-screen'
import { AUTO_REFRESH_INTERVAL_MS } from '@/hooks/use-auto-refresh'
import type { AdminSalon } from '@/lib/admin-catalogue'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Вкладка «Салоны» админского каталога: очередь проверки и решения.
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания экрана, которых не видит
 * unit-тест транспорта: фильтр `status=pending`, owner email и бейдж в строке,
 * `alertdialog` с последствием одобрения, обязательную причину отказа и то, что
 * после решения строка уходит из очереди.
 */

const LIST_URL = '/api/admin/catalogue/salons'
const SALON_ID = '9e400000-0000-4000-8000-000000000077'
const OWNER_EMAIL = 'e2e-barber@barbersearch.test'

const PENDING_SALON: AdminSalon = {
  id: SALON_ID,
  name: 'E2E Studio Проверка',
  address: 'ул. Проверочная, 5',
  city: 'Москва',
  postalCode: null,
  country: 'Russia',
  timeZoneId: 'Europe/Moscow',
  latitude: 55.75,
  longitude: 37.62,
  phoneNumber: '+70000000055',
  websiteUrl: null,
  isActive: false,
  moderationStatus: 'Pending',
  rejectionReason: null,
  reviewedAt: null,
  ownerEmail: OWNER_EMAIL,
  usageCount: 1,
}

type ListMock = {
  statuses: string[]
}

function mockList(rows: AdminSalon[]): ListMock {
  const state: ListMock = { statuses: [] }

  server.use(
    http.get(LIST_URL, ({ request }) => {
      const status = new URL(request.url).searchParams.get('status') ?? ''

      state.statuses.push(status)

      const filtered = rows.filter((row) => {
        switch (status) {
          case 'pending':
            return row.moderationStatus === 'Pending'
          case 'active':
            return row.isActive && row.moderationStatus === 'Active'
          case 'hidden':
            return !row.isActive && row.moderationStatus === 'Active'
          default:
            return true
        }
      })

      return HttpResponse.json(filtered)
    })
  )

  return state
}

function tableRow(name: string): HTMLElement {  const row = screen
    .getAllByRole('row')
    .find((candidate) => within(candidate).queryByText(name) !== null)

  if (!row) {
    throw new Error(`Строка «${name}» не найдена`)
  }

  return row
}

async function openPendingFilter(): Promise<void> {
  render(<CatalogueScreen tab="salons" />)

  await screen.findAllByText(PENDING_SALON.name)
  await userEvent
    .setup()
    .click(screen.getByRole('button', { name: 'На проверке' }))
}

function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    get: () => state,
  })
  document.dispatchEvent(new Event('visibilitychange'))
}

describe('CatalogueScreen: салоны на проверке', () => {
  afterEach(() => {
    vi.useRealTimers()
    Reflect.deleteProperty(document, 'visibilityState')
  })

  it('фильтр «На проверке» запрашивает status=pending и показывает владельца', async () => {
    const list = mockList([PENDING_SALON])

    await openPendingFilter()

    await waitFor(() => {
      expect(list.statuses).toContain('pending')
    })

    // pending есть только у салонов: у услуг и специализаций кнопки нет.
    expect(
      screen.getByRole('button', { name: 'На проверке' })
    ).toBeInTheDocument()
    expect(screen.getAllByText('На проверке').length).toBeGreaterThan(0)

    expect(screen.getAllByText(OWNER_EMAIL).length).toBeGreaterThan(0)
    expect(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Одобрить',
      })
    ).toBeInTheDocument()
    expect(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Отклонить',
      })
    ).toBeInTheDocument()
    // Активация неодобренного салона вернула бы 409 — кнопки нет.
    expect(
      within(tableRow(PENDING_SALON.name)).queryByRole('button', {
        name: 'Включить',
      })
    ).not.toBeInTheDocument()
  })

  it('одобрение: alertdialog объясняет последствие, POST без тела, строка уходит', async () => {
    const user = userEvent.setup()
    const rows = [{ ...PENDING_SALON }]
    const list = mockList(rows)
    const calls: { method: string; path: string; body: string }[] = []

    server.use(
      http.post(`/api/salons/${SALON_ID}/approval`, async ({ request }) => {
        calls.push({
          method: request.method,
          path: new URL(request.url).pathname,
          body: await request.text(),
        })

        rows[0] = { ...rows[0], moderationStatus: 'Active', isActive: true }

        return new HttpResponse(null, { status: 204 })
      })
    )

    render(<CatalogueScreen tab="salons" />)
    await screen.findAllByText(PENDING_SALON.name)
    await user.click(screen.getByRole('button', { name: 'На проверке' }))
    await waitFor(() => {
      expect(list.statuses).toContain('pending')
    })

    await user.click(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Одобрить',
      })
    )

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).getByText(
        'Салон появится в поиске и станет доступен для записи.'
      )
    ).toBeInTheDocument()
    expect(within(dialog).getByText(/E2E Studio Проверка/)).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Одобрить' }))

    await waitFor(() => {
      expect(calls).toStrictEqual([
        {
          method: 'POST',
          path: `/api/salons/${SALON_ID}/approval`,
          body: '',
        },
      ])
    })

    expect(
      await screen.findByText(
        `Салон «${PENDING_SALON.name}» одобрен — он появился в поиске.`
      )
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryAllByText(PENDING_SALON.name)).toStrictEqual([])
    })
    expect(screen.getByText('Салоны на проверке')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('отказ без причины не отправляет запрос', async () => {
    const user = userEvent.setup()
    const list = mockList([PENDING_SALON])
    const posts: string[] = []

    server.use(
      http.post(`/api/salons/${SALON_ID}/rejection`, () => {
        posts.push('rejection')

        return new HttpResponse(null, { status: 204 })
      })
    )

    render(<CatalogueScreen tab="salons" />)
    await screen.findAllByText(PENDING_SALON.name)
    await user.click(screen.getByRole('button', { name: 'На проверке' }))
    await waitFor(() => {
      expect(list.statuses).toContain('pending')
    })

    await user.click(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Отклонить',
      })
    )

    const dialog = screen.getByRole('alertdialog')

    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(
      within(dialog).getByText('Укажите причину отказа.')
    ).toBeInTheDocument()
    expect(posts).toStrictEqual([])
  })

  it('отказ отправляет причину и убирает строку из очереди', async () => {
    const user = userEvent.setup()
    const rows = [{ ...PENDING_SALON }]
    const list = mockList(rows)
    const bodies: unknown[] = []

    server.use(
      http.post(`/api/salons/${SALON_ID}/rejection`, async ({ request }) => {
        bodies.push(await request.json())
        rows[0] = {
          ...rows[0],
          moderationStatus: 'Rejected',
          rejectionReason: 'Не подтверждён адрес',
        }

        return new HttpResponse(null, { status: 204 })
      })
    )

    render(<CatalogueScreen tab="salons" />)
    await screen.findAllByText(PENDING_SALON.name)
    await user.click(screen.getByRole('button', { name: 'На проверке' }))
    await waitFor(() => {
      expect(list.statuses).toContain('pending')
    })

    await user.click(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Отклонить',
      })
    )

    const dialog = screen.getByRole('alertdialog')

    await user.type(
      within(dialog).getByLabelText('Причина отказа'),
      'Не подтверждён адрес'
    )
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    await waitFor(() => {
      expect(bodies).toStrictEqual([{ reason: 'Не подтверждён адрес' }])
    })

    expect(
      await screen.findByText(
        `Салон «${PENDING_SALON.name}» отклонён. Владелец увидит причину.`
      )
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.queryAllByText(PENDING_SALON.name)).toStrictEqual([])
    })
  })

  it('400 причины и 409 повторного решения показываются в диалоге', async () => {
    const user = userEvent.setup()
    const list = mockList([PENDING_SALON])

    server.use(
      http.post(`/api/salons/${SALON_ID}/rejection`, () =>
        HttpResponse.json(
          problemDetails(400, { errorCodes: ['salon.reject.reasonTooLong'] }),
          { status: 400 }
        )
      )
    )

    render(<CatalogueScreen tab="salons" />)
    await screen.findAllByText(PENDING_SALON.name)
    await user.click(screen.getByRole('button', { name: 'На проверке' }))
    await waitFor(() => {
      expect(list.statuses).toContain('pending')
    })

    await user.click(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Отклонить',
      })
    )

    let dialog = screen.getByRole('alertdialog')

    await user.type(within(dialog).getByLabelText('Причина отказа'), 'Причина')
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(
      await within(dialog).findByText(
        'Слишком длинная причина — максимум 500 символов.'
      )
    ).toBeInTheDocument()
    expect(dialog).toBeInTheDocument()

    await user.click(
      within(dialog).getByRole('button', { name: 'Оставить как есть' })
    )

    server.use(
      http.post(`/api/salons/${SALON_ID}/approval`, () =>
        HttpResponse.json(
          problemDetails(409, { errorCodes: ['salon.review.invalidStatus'] }),
          { status: 409 }
        )
      )
    )

    const requestsBefore = list.statuses.length

    await user.click(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Одобрить',
      })
    )

    dialog = screen.getByRole('alertdialog')
    await user.click(within(dialog).getByRole('button', { name: 'Одобрить' }))

    expect(
      await within(dialog).findByText('Салон уже проверили. Обновите список.')
    ).toBeInTheDocument()
    // 409 перечитывает список: строка могла измениться в другой вкладке.
    await waitFor(() => {
      expect(list.statuses.length).toBeGreaterThan(requestsBefore)
    })
  })

  it('403 в действии объясняет, что раздел только для администратора', async () => {
    const user = userEvent.setup()
    mockList([PENDING_SALON])

    server.use(
      http.post(`/api/salons/${SALON_ID}/approval`, () =>
        HttpResponse.json(problemDetails(403), { status: 403 })
      )
    )

    render(<CatalogueScreen tab="salons" />)
    await screen.findAllByText(PENDING_SALON.name)
    await user.click(screen.getByRole('button', { name: 'На проверке' }))
    await screen.findAllByText(PENDING_SALON.name)

    await user.click(
      within(tableRow(PENDING_SALON.name)).getByRole('button', {
        name: 'Одобрить',
      })
    )

    const dialog = screen.getByRole('alertdialog')

    await user.click(within(dialog).getByRole('button', { name: 'Одобрить' }))

    expect(
      await within(dialog).findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()
  })

  it('не опрашивает справочник по таймеру, но перечитывает при возврате на вкладку', async () => {
    vi.useFakeTimers()
    const state = mockList([PENDING_SALON])

    render(<CatalogueScreen tab="salons" />)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    const initial = state.statuses.length

    expect(initial).toBeGreaterThanOrEqual(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(AUTO_REFRESH_INTERVAL_MS)
    })
    expect(state.statuses).toHaveLength(initial)

    await act(async () => {
      setVisibility('hidden')
    })
    await act(async () => {
      setVisibility('visible')
    })

    expect(state.statuses).toHaveLength(initial + 1)
  })
})
