import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { BarberDay } from '@/components/barber/barber-day'
import type { BarberDayController } from '@/hooks/use-barber-day'
import type { DayGap } from '@/lib/barber-day'
import type { BarberBooking } from '@/lib/barber-bookings'
import { stubImageLoading } from '@/test/image'

/**
 * Лента дня: порядок строк и окон, шапка дня и состояния.
 *
 * Время строки форматируется в поясе салона, поэтому в фикстурах пояс `UTC`, а
 * моменты — в UTC: текст не зависит от пояса машины, на которой идёт тест.
 */

const NOW = Date.UTC(2026, 10, 8, 12, 0, 0)

function utc(hours: number, minutes = 0): string {
  return new Date(Date.UTC(2026, 10, 8, hours, minutes, 0)).toISOString()
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
    price: 2400,
    customerNote: null,
    barberNote: null,
    cancellationReason: null,
    customer: { displayName: 'Иван Петров', phoneNumber: null, avatarUrl: null },
    service: { name: 'Стрижка' },
    salon: {
      name: 'Барбершоп на Ленина',
      address: 'ул. Ленина, 1',
      city: 'Екатеринбург',
      timeZoneId: 'UTC',
    },
  }
}

function controller(
  overrides: Partial<BarberDayController> = {}
): BarberDayController {
  return {
    items: [],
    gaps: [],
    nextBooking: null,
    isTruncated: false,
    status: 'ready',
    error: null,
    partialError: null,
    retryFailed: vi.fn(),
    requiresSignIn: false,
    isRefreshing: false,
    refreshError: null,
    updatedAt: null,
    refresh: vi.fn(async () => null),
    reload: vi.fn(),
    confirm: vi.fn(async () => undefined),
    complete: vi.fn(async () => undefined),
    markNoShow: vi.fn(async () => undefined),
    cancel: vi.fn(async () => undefined),
    saveNote: vi.fn(async () => undefined),
    reschedule: vi.fn(async () => undefined),
    reprice: vi.fn(async () => undefined),
    ...overrides,
  }
}

function draw(day: BarberDayController) {
  const callbacks = {
    onFillGap: vi.fn(),
    onCreate: vi.fn(),
    onConfirm: vi.fn(async () => undefined),
    onComplete: vi.fn(async () => undefined),
    onOpenCancel: vi.fn(),
    onOpenNoShow: vi.fn(),
    onOpenNote: vi.fn(),
    onOpenReschedule: vi.fn(),
    onOpenPrice: vi.fn(),
    onReload: vi.fn(),
    registerRow: vi.fn(),
  }

  const result = render(
    <BarberDay
      day={day}
      now={NOW}
      headingRef={{ current: null }}
      {...callbacks}
    />
  )

  return { ...callbacks, container: result.container }
}

describe('BarberDay: лента дня', () => {
  it('рисует записи и окна в порядке времени', () => {
    const gap: DayGap = { startAt: utc(10, 30), endAt: utc(11), minutes: 30 }

    draw(
      controller({
        items: [
          booking('a', utc(10), utc(10, 30), 'Completed'),
          booking('b', utc(11), utc(11, 30), 'Pending'),
        ],
        gaps: [gap],
      })
    )

    const list = screen.getByRole('list', { name: 'Расписание на сегодня' })
    const items = within(list).getAllByRole('listitem')

    expect(items).toHaveLength(3)
    expect(items[0]).toHaveTextContent('10:00')
    expect(items[1]).toHaveTextContent('свободно 30 мин')
    expect(items[2]).toHaveTextContent('11:00')
  })

  it('шапка считает записи и окна дня', () => {
    draw(
      controller({
        items: [
          booking('a', utc(10), utc(10, 30), 'Completed'),
          booking('b', utc(11), utc(11, 30)),
          booking('c', utc(13), utc(13, 30)),
        ],
        gaps: [
          { startAt: utc(10, 30), endAt: utc(11), minutes: 30 },
          { startAt: utc(11, 30), endAt: utc(13), minutes: 90 },
        ],
      })
    )

    expect(screen.getByRole('heading', { name: 'Сегодня' })).toBeVisible()
    expect(
      screen.getByText(/3 записи · 2 окна/)
    ).toBeVisible()
  })

  it('«Занять» в окне отдаёт это окно наверх', async () => {
    const gap: DayGap = { startAt: utc(10, 30), endAt: utc(11), minutes: 30 }
    const user = userEvent.setup()

    const callbacks = draw(
      controller({
        items: [
          booking('a', utc(10), utc(10, 30)),
          booking('b', utc(11), utc(11, 30)),
        ],
        gaps: [gap],
      })
    )

    await user.click(
      screen.getByRole('button', { name: 'Занять время с 10:30 до 11:00' })
    )

    expect(callbacks.onFillGap).toHaveBeenCalledWith(gap)
  })

  it('пустой день предлагает записать клиента', async () => {
    const user = userEvent.setup()
    const callbacks = draw(controller())

    expect(
      screen.getByText('На сегодня записей нет.', { selector: 'p.font-medium' })
    ).toBeVisible()
    expect(
      screen.getByText('Запишите клиента, если он придёт без записи.')
    ).toBeVisible()

    await user.click(screen.getByRole('button', { name: 'Записать клиента' }))

    expect(callbacks.onCreate).toHaveBeenCalledTimes(1)
  })
})

describe('BarberDay: следующий клиент', () => {
  it('отсчёт до будущего визита', () => {
    draw(
      controller({
        items: [booking('next', utc(12, 40), utc(13, 10))],
        nextBooking: booking('next', utc(12, 40), utc(13, 10)),
      })
    )

    expect(screen.getByText('Следующий клиент через 40 минут')).toBeVisible()
  })

  it('идущий визит важнее отсчёта', () => {
    const ongoing = booking('ongoing', utc(11), utc(15, 30))

    draw(controller({ items: [ongoing], nextBooking: ongoing }))

    expect(screen.getByText('Идёт визит до 15:30')).toBeVisible()
  })

  it('далёкий визит показывается временем, а не отсчётом', () => {
    const far = booking(
      'far',
      new Date(Date.UTC(2026, 10, 20, 18, 30)).toISOString(),
      new Date(Date.UTC(2026, 10, 20, 19, 0)).toISOString()
    )

    draw(controller({ items: [far], nextBooking: far }))

    expect(screen.getByText('Следующий клиент в 18:30')).toBeVisible()
  })
})

describe('BarberDay: аватар клиента', () => {
  const AVATAR_URL =
    '/api/media/avatars/9e000000-0000-4000-8000-000000000115/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

  it('показывает фото клиента, когда оно есть', async () => {
    stubImageLoading()

    const withAvatar: BarberBooking = {
      ...booking('a', utc(10), utc(10, 30)),
      customer: {
        displayName: 'Иван Петров',
        phoneNumber: null,
        avatarUrl: AVATAR_URL,
      },
    }

    const { container } = draw(controller({ items: [withAvatar] }))

    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })

    // Инициалы не дублируют фото.
    expect(screen.queryByText('ИП')).not.toBeInTheDocument()
  })

  it('у гостя без фото остаются инициалы', () => {
    draw(controller({ items: [booking('a', utc(10), utc(10, 30))] }))

    expect(screen.getByText('ИП')).toBeInTheDocument()
    expect(document.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })
})

describe('BarberDay: состояния', () => {
  it('первая загрузка показывает скелетон', () => {
    draw(controller({ status: 'loading' }))

    expect(screen.getByRole('heading', { name: 'Сегодня' })).toBeVisible()
    expect(screen.queryByText('На сегодня записей нет.')).toBeNull()
  })

  it('ошибка дня: текст и «Повторить»', async () => {
    const user = userEvent.setup()
    const retryFailed = vi.fn()

    draw(
      controller({
        status: 'error',
        error: 'Сервер недоступен.',
        retryFailed,
      })
    )

    expect(screen.getByText('Не удалось загрузить записи.')).toBeVisible()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(retryFailed).toHaveBeenCalledTimes(1)
  })

  it('частичная ошибка: лента остаётся, сверху предупреждение', () => {
    draw(
      controller({
        items: [booking('a', utc(10), utc(10, 30))],
        partialError: 'Не удалось обновить часть записей.',
      })
    )

    expect(
      screen.getByText('Не удалось обновить часть записей.')
    ).toBeVisible()
    expect(
      screen.getByRole('list', { name: 'Расписание на сегодня' })
    ).toBeVisible()
  })

  it('обрезка дня подписана честно', () => {
    draw(
      controller({
        items: [booking('a', utc(10), utc(10, 30))],
        isTruncated: true,
      })
    )

    expect(screen.getByText('Показаны первые 20 записей дня.')).toBeVisible()
  })
})
