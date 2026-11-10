import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { BookingsScreen } from '@/components/admin/bookings-screen'
import { AUTO_REFRESH_INTERVAL_MS } from '@/hooks/use-auto-refresh'
import type { AdminBooking } from '@/lib/admin-bookings'
import { problemDetails, unauthorized } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Экран броней: список, фильтры и состояния.
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания, которые не видит
 * unit-тест транспорта: строки списка, смену фильтра (новая первая страница),
 * ошибку следующей страницы без стирания строк и пустые состояния. Русская
 * копия сравнивается целиком.
 */

const BOOKING: AdminBooking = {
  id: '9e900000-0000-4000-8000-000000000001',
  status: 'Pending',
  startAt: '2026-09-12T20:30:00+00:00',
  endAt: '2026-09-12T21:00:00+00:00',
  price: 30,
  customerNote: null,
  barberNote: null,
  cancellationReason: null,
  createdAt: '2026-09-11T12:00:00+00:00',
  customer: {
    id: '9e000000-0000-4000-8000-000000000009',
    firstName: 'Ирина',
    lastName: 'Волкова',
    email: 'e2e-bookings@barbersearch.test',
    phoneNumber: '+70000000009',
  },
  barber: {
    profileId: '9e100000-0000-4000-8000-000000000001',
    displayName: 'Артём Соколов',
  },
  service: { name: 'Стрижка' },
  salon: {
    id: '9e400000-0000-4000-8000-000000000001',
    name: 'BarberSearch Тверская',
    address: 'ул. Тверская, 12',
    city: 'Москва',
    timeZoneId: 'Europe/Moscow',
  },
}

/** Ответ списка с запоминанием параметров каждого запроса. */
function mockList(
  items: AdminBooking[],
  nextCursor: string | null = null
): URLSearchParams[] {
  const requests: URLSearchParams[] = []

  server.use(
    http.get('/api/admin/bookings', ({ request }) => {
      requests.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items, nextCursor })
    })
  )

  return requests
}

/**
 * Ширина колонки списка в jsdom.
 *
 * `ResizeObserver` в jsdom нет, поэтому таблица броней не включается сама:
 * тест подменяет наблюдатель и сам сообщает измеренную ширину. Возвращает
 * функцию, которая «доставляет» ширину после того, как список отрисован.
 */
function stubListWidth(width: number): () => void {
  const callbacks: Array<
    (entries: Array<{ contentRect: { width: number } }>) => void
  > = []

  class ResizeObserverStub {
    constructor(
      callback: (entries: Array<{ contentRect: { width: number } }>) => void
    ) {
      callbacks.push(callback)
    }

    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }

  vi.stubGlobal('ResizeObserver', ResizeObserverStub)

  return () => {
    act(() => {
      for (const callback of callbacks) {
        callback([{ contentRect: { width } }])
      }
    })
  }
}

describe('BookingsScreen', () => {
  afterEach(() => {
    vi.useRealTimers()
  })
  it('показывает заголовок, подпись, строки и ссылку на карточку', async () => {
    mockList([BOOKING])

    render(<BookingsScreen bookingId={null} />)

    expect(
      screen.getByRole('heading', { name: 'Брони', level: 1 })
    ).toBeInTheDocument()
    expect(
      screen.getByText('Все записи клиентов. Время показано в поясе салона.')
    ).toBeInTheDocument()

    const list = await screen.findByRole('list', { name: 'Список броней' })

    expect(within(list).getByText('Ирина Волкова')).toBeInTheDocument()
    expect(
      within(list).getByText('e2e-bookings@barbersearch.test')
    ).toBeInTheDocument()
    expect(within(list).getByText('Артём Соколов')).toBeInTheDocument()
    expect(within(list).getByText('Стрижка')).toBeInTheDocument()
    expect(within(list).getAllByText(/23:30/).length).toBeGreaterThan(0)
    expect(within(list).getByText('Ждёт подтверждения')).toBeInTheDocument()

    const link = within(list).getByRole('link')

    expect(link).toHaveAttribute('href', `/admin/bookings/${BOOKING.id}`)
  })

  it('в широкой колонке показывает таблицу с ценами и статусами', async () => {
    mockList([BOOKING])
    const notify = stubListWidth(600)

    render(<BookingsScreen bookingId={null} />)

    await screen.findByRole('list', { name: 'Список броней' })
    notify()

    const table = await screen.findByRole('table', { name: 'Список броней' })

    expect(
      within(table).getByRole('columnheader', { name: 'Статус' })
    ).toBeInTheDocument()
    expect(within(table).getByText('Ждёт подтверждения')).toBeInTheDocument()
    expect(within(table).getByText('30 ₽')).toBeInTheDocument()
    expect(
      screen.queryByRole('list', { name: 'Список броней' })
    ).not.toBeInTheDocument()
  })

  it('в узкой колонке оставляет карточки вместо таблицы', async () => {
    mockList([BOOKING])
    const notify = stubListWidth(420)

    render(<BookingsScreen bookingId={null} />)

    await screen.findByRole('list', { name: 'Список броней' })
    notify()

    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(
      screen.getByRole('list', { name: 'Список броней' })
    ).toBeInTheDocument()
  })

  it('показывает гостя без аккаунта по телефону вместо email', async () => {
    mockList([
      {
        ...BOOKING,
        customer: {
          id: null,
          firstName: 'Анна',
          lastName: 'Смирнова',
          email: null,
          phoneNumber: '+79991112233',
        },
      },
    ])

    render(<BookingsScreen bookingId={null} />)

    const list = await screen.findByRole('list', { name: 'Список броней' })

    expect(within(list).getByText('Анна Смирнова')).toBeInTheDocument()
    expect(within(list).getByText('+79991112233')).toBeInTheDocument()
  })

  it('выбранная строка помечена aria-current, а карточка открывается по id', async () => {
    mockList([BOOKING])
    server.use(
      http.get('/api/admin/bookings/:bookingId', () =>
        HttpResponse.json(BOOKING)
      )
    )

    render(<BookingsScreen bookingId={BOOKING.id} />)

    const list = await screen.findByRole('list', { name: 'Список броней' })

    expect(within(list).getByRole('link')).toHaveAttribute(
      'aria-current',
      'page'
    )

    const detail = await screen.findByRole('heading', {
      name: 'Бронь',
      level: 2,
    })

    expect(detail).toBeInTheDocument()
  })

  it('смена статуса начинает первую страницу и не сбрасывает поиск', async () => {
    const user = userEvent.setup()
    const requests = mockList([BOOKING])

    render(<BookingsScreen bookingId={null} />)

    await screen.findByRole('list', { name: 'Список броней' })

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Статус' }),
      'Confirmed'
    )

    await waitFor(() => {
      expect(requests.at(-1)?.get('status')).toBe('Confirmed')
    })
    expect(requests.at(-1)?.has('cursor')).toBe(false)

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Статус' }),
      'NoShow'
    )

    await waitFor(() => {
      expect(requests.at(-1)?.get('status')).toBe('NoShow')
    })
  })

  it('поиск отправляется с задержкой и только непустым', async () => {
    const user = userEvent.setup()
    const requests = mockList([BOOKING])

    render(<BookingsScreen bookingId={null} />)

    await screen.findByRole('list', { name: 'Список броней' })

    await user.type(
      screen.getByRole('searchbox', { name: 'Клиент или номер брони' }),
      'e2e-bookings'
    )

    await waitFor(
      () => {
        expect(requests.at(-1)?.get('search')).toBe('e2e-bookings')
      },
      { timeout: 2000 }
    )

    expect(requests.at(-1)?.get('status')).toBe('all')
    expect(requests.at(-1)?.has('cursor')).toBe(false)
  })

  it('«Сбросить фильтры» возвращает статус и поиск к значениям по умолчанию', async () => {
    const user = userEvent.setup()
    const requests = mockList([BOOKING])

    render(<BookingsScreen bookingId={null} />)

    await screen.findByRole('list', { name: 'Список броней' })

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Статус' }),
      'Pending'
    )
    await user.click(
      await screen.findByRole('button', { name: 'Сбросить фильтры' })
    )

    await waitFor(() => {
      expect(requests.at(-1)?.get('status')).toBe('all')
    })
    expect(requests.at(-1)?.has('search')).toBe(false)
    expect(
      screen.queryByRole('button', { name: 'Сбросить фильтры' })
    ).not.toBeInTheDocument()
  })

  it('без фильтров пустой ответ показывает «Броней нет»', async () => {
    mockList([])

    render(<BookingsScreen bookingId={null} />)

    expect(await screen.findByText('Броней нет')).toBeInTheDocument()
  })

  it('с фильтром пустой ответ показывает «Ничего не найдено»', async () => {
    const user = userEvent.setup()

    mockList([])
    render(<BookingsScreen bookingId={null} />)

    await screen.findByText('Броней нет')

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Статус' }),
      'NoShow'
    )

    expect(await screen.findByText('Ничего не найдено')).toBeInTheDocument()
  })

  it('403 показывает текст про роль, а не английский detail', async () => {
    server.use(
      http.get('/api/admin/bookings', () =>
        HttpResponse.json(
          problemDetails(403, {
            detail: 'Forbidden.',
            errorCodes: ['auth.forbidden'],
          }),
          { status: 403 }
        )
      )
    )

    render(<BookingsScreen bookingId={null} />)

    expect(
      await screen.findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()
    expect(screen.getByText('Не удалось загрузить брони')).toBeInTheDocument()
    expect(screen.queryByText('Forbidden.')).not.toBeInTheDocument()
  })

  it('401 внутри экрана показывает гейт входа', async () => {
    server.use(http.get('/api/admin/bookings', () => unauthorized()))

    render(<BookingsScreen bookingId={null} />)

    expect(
      await screen.findByText('Вход для администраторов')
    ).toBeInTheDocument()
  })

  it('ошибка следующей страницы не стирает показанные строки', async () => {
    const user = userEvent.setup()

    server.use(
      http.get('/api/admin/bookings', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')

        if (cursor === null) {
          return HttpResponse.json({
            items: [BOOKING],
            nextCursor: 'cursor-2',
          })
        }

        return HttpResponse.json(
          problemDetails(500, { detail: 'Boom.', errorCodes: ['server.error'] }),
          { status: 500 }
        )
      })
    )

    render(<BookingsScreen bookingId={null} />)

    const list = await screen.findByRole('list', { name: 'Список броней' })

    await user.click(
      screen.getByRole('button', { name: 'Дальше — следующая страница' })
    )

    expect(
      await screen.findByText('Не удалось загрузить следующую страницу броней.')
    ).toBeInTheDocument()
    expect(within(list).getByText('Ирина Волкова')).toBeInTheDocument()
    expect(
      screen.getByRole('button', {
        name: 'Повторить — загрузить следующую страницу',
      })
    ).toBeInTheDocument()
  })

  it('в списке нет действий и лишних кнопок', async () => {
    mockList([BOOKING])

    render(<BookingsScreen bookingId={null} />)

    const list = await screen.findByRole('list', { name: 'Список броней' })

    expect(within(list).queryAllByRole('button')).toHaveLength(0)

    for (const text of [
      'Создать бронь',
      'Показать ещё',
      'Перенести',
      'Редактировать',
      'Удалить',
    ]) {
      expect(screen.queryByText(text)).not.toBeInTheDocument()
    }
  })

  it('через период опроса перечитывает список сам', async () => {
    vi.useFakeTimers()
    const requests = mockList([BOOKING])

    render(<BookingsScreen bookingId={null} />)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    const initial = requests.length

    expect(initial).toBeGreaterThanOrEqual(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(AUTO_REFRESH_INTERVAL_MS)
    })

    expect(requests).toHaveLength(initial + 1)
  })

  it('через период опроса перечитывает открытую карточку', async () => {
    vi.useFakeTimers()
    mockList([BOOKING])

    let detailRequests = 0

    server.use(
      http.get(`/api/admin/bookings/${BOOKING.id}`, () => {
        detailRequests += 1

        return HttpResponse.json(BOOKING)
      })
    )

    render(<BookingsScreen bookingId={BOOKING.id} />)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    const initial = detailRequests

    expect(initial).toBeGreaterThanOrEqual(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(AUTO_REFRESH_INTERVAL_MS)
    })

    expect(detailRequests).toBe(initial + 1)
  })
})
