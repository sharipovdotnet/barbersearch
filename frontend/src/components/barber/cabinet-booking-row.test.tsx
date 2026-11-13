import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { CabinetBookingRow } from '@/components/barber/cabinet-booking-row'
import type { BarberBooking } from '@/lib/barber-bookings'
import { stubImageLoading } from '@/test/image'

/**
 * Строка записи клиента: аватар перед именем и телефон-ссылка.
 *
 * У гостя (walk-in) фото нет — остаются инициалы из имени брони; имя в обоих
 * случаях остаётся текстом, а изображение декоративно.
 */

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000115/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

function booking(avatarUrl: string | null): BarberBooking {
  return {
    id: '77777777-7777-4777-8777-777777777777',
    status: 'Confirmed',
    startAt: '2026-11-08T10:00:00Z',
    endAt: '2026-11-08T10:30:00Z',
    price: 2400,
    customerNote: null,
    barberNote: null,
    cancellationReason: null,
    customer: {
      displayName: 'Пётр Гостев',
      phoneNumber: '+79990000000',
      avatarUrl,
    },
    service: { name: 'Стрижка' },
    salon: {
      name: 'Барбершоп на Ленина',
      address: 'ул. Ленина, 1',
      city: 'Екатеринбург',
      timeZoneId: 'UTC',
    },
  }
}

function renderRow(value: BarberBooking, now = Date.UTC(2026, 10, 8, 12, 0, 0)) {
  return render(
    <ul>
      <CabinetBookingRow
        booking={value}
        scope="upcoming"
        now={now}
        onConfirm={vi.fn(async () => undefined)}
        onComplete={vi.fn(async () => undefined)}
        onOpenCancel={vi.fn()}
        onOpenNoShow={vi.fn()}
        onOpenNote={vi.fn()}
        onOpenReschedule={vi.fn()}
        onOpenPrice={vi.fn()}
        onReload={vi.fn()}
        registerRow={vi.fn()}
      />
    </ul>
  )
}

describe('CabinetBookingRow: аватар клиента', () => {
  it('показывает фото клиента и оставляет телефон tel:-ссылкой', async () => {
    stubImageLoading()

    const { container } = renderRow(booking(AVATAR_URL))

    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })
    expect(container.querySelector('[data-slot="avatar-image"]')).toHaveAttribute(
      'alt',
      ''
    )
    expect(screen.getByText('Пётр Гостев')).toBeInTheDocument()
    expect(screen.getByRole('link')).toHaveAttribute(
      'href',
      'tel:+79990000000'
    )
    // Инициалы не дублируют фото.
    expect(screen.queryByText('ПГ')).not.toBeInTheDocument()
  })

  it('у гостя без фото показывает инициалы и телефон', () => {
    renderRow(booking(null))

    expect(screen.getByText('ПГ')).toBeInTheDocument()
    expect(document.querySelector('[data-slot="avatar-image"]')).toBeNull()
    expect(screen.getByRole('link', { name: '+79990000000' })).toHaveAttribute(
      'href',
      'tel:+79990000000'
    )
  })
})

describe('CabinetBookingRow: перенос', () => {
  it('предлагает перенос будущей записи с временем в имени', () => {
    renderRow(booking(null), Date.UTC(2026, 10, 8, 8, 0, 0))

    expect(
      screen.getByRole('button', { name: /^Перенести запись/ })
    ).toBeInTheDocument()
  })

  it('не предлагает перенос начавшейся записи', () => {
    renderRow(booking(null), Date.UTC(2026, 10, 8, 12, 0, 0))

    expect(
      screen.queryByRole('button', { name: /^Перенести запись/ })
    ).not.toBeInTheDocument()
  })
})

describe('CabinetBookingRow: цена визита', () => {
  it('предлагает правку цены у подтверждённой записи', () => {
    renderRow(booking(null))

    const button = screen.getByRole('button', {
      name: /^Изменить цену визита/,
    })

    expect(button).toHaveAttribute('title', 'Изменить цену визита')
  })

  it('предлагает правку цены у завершённой записи: блок действий там не рисуется', () => {
    renderRow({ ...booking(null), status: 'Completed' })

    // Блока подтверждения/завершения/отмены у завершённой строки нет,
    // но кнопка цены живёт рядом с суммой и остаётся.
    expect(screen.queryByRole('button', { name: 'Отменить' })).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: /^Изменить цену визита/ })
    ).toBeInTheDocument()
  })

  it('не предлагает правку цены у отменённой записи и у неявки', () => {
    const { unmount } = renderRow({ ...booking(null), status: 'Cancelled' })

    expect(
      screen.queryByRole('button', { name: /^Изменить цену визита/ })
    ).not.toBeInTheDocument()

    unmount()
    renderRow({ ...booking(null), status: 'NoShow' })

    expect(
      screen.queryByRole('button', { name: /^Изменить цену визита/ })
    ).not.toBeInTheDocument()
  })
})
