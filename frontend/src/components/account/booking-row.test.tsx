import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { BookingRow } from '@/components/account/booking-row'
import type { BookingView } from '@/components/account/booking-view'
import { stubImageLoading } from '@/test/image'

const VIEW: BookingView = {
  booking: {
    id: '22222222-2222-4222-8222-222222222222',
    userId: '33333333-3333-4333-8333-333333333333',
    barberProfileId: '44444444-4444-4444-8444-444444444444',
    barberServiceId: '55555555-5555-4555-8555-555555555555',
    salonId: '66666666-6666-4666-8666-666666666666',
    startAt: '2026-12-01T10:00:00Z',
    endAt: '2026-12-01T10:30:00Z',
    price: 45,
    status: 'Cancelled',
    idempotencyKey: 'booking-key-1',
    customerNote: null,
    barberNote: null,
    cancellationReason: 'Барбер заболел',
    createdAt: '2026-11-01T10:00:00Z',
    hasReview: false,
  },
  barberName: 'Барбер Номер 1',
  barberAvatarUrl: null,
  serviceName: 'Стрижка',
  salonName: 'Салон',
  salonAddress: 'ул. Тестовая, 1',
  salonCity: 'Берлин',
  timeZoneId: 'Europe/Berlin',
  durationMinutes: 30,
  isResolved: true,
}

function renderRow(
  overrides: Partial<BookingView['booking']> = {},
  isReviewed = false,
  now = Date.parse('2026-11-01T10:00:00Z')
) {
  return render(
    <ul>
      <BookingRow
        view={{ ...VIEW, booking: { ...VIEW.booking, ...overrides } }}
        isReviewed={isReviewed}
        now={now}
        onRequestReschedule={vi.fn()}
        onRequestCancel={vi.fn()}
        onRequestReview={vi.fn()}
      />
    </ul>
  )
}

function renderView(overrides: Partial<BookingView> = {}) {
  return render(
    <ul>
      <BookingRow
        view={{ ...VIEW, ...overrides }}
        isReviewed={false}
        now={Date.parse('2026-11-01T10:00:00Z')}
        onRequestReschedule={vi.fn()}
        onRequestCancel={vi.fn()}
        onRequestReview={vi.fn()}
      />
    </ul>
  )
}

describe('BookingRow, отзыв', () => {
  it('предлагает отзыв по завершённому визиту без отзыва', () => {
    renderRow({ status: 'Completed', hasReview: false })

    expect(
      screen.getByRole('button', { name: /Оставить отзыв о визите/ })
    ).toBeInTheDocument()
  })

  it('скрывает звезду, если отзыв уже оставлен на сервере', () => {
    renderRow({ status: 'Completed', hasReview: true })

    expect(
      screen.queryByRole('button', { name: /Оставить отзыв о визите/ })
    ).not.toBeInTheDocument()
    expect(screen.getByText('Отзыв отправлен')).toBeInTheDocument()
  })

  it('скрывает звезду сразу после отправки отзыва', () => {
    renderRow({ status: 'Completed', hasReview: false }, true)

    expect(
      screen.queryByRole('button', { name: /Оставить отзыв о визите/ })
    ).not.toBeInTheDocument()
    expect(screen.getByText('Отзыв отправлен')).toBeInTheDocument()
  })
})

describe('BookingRow, аватар барбера', () => {
  const AVATAR_URL =
    '/api/media/avatars/9e000000-0000-4000-8000-000000000115/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

  it('показывает фото барбера, когда оно есть', async () => {
    stubImageLoading()

    const { container } = renderView({ barberAvatarUrl: AVATAR_URL })

    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })
    expect(screen.getByText('Барбер Номер 1')).toBeInTheDocument()
    expect(screen.queryByText('БН')).not.toBeInTheDocument()
  })

  it('без фото показывает инициалы барбера', () => {
    renderView()

    expect(screen.getByText('БН')).toBeInTheDocument()
    expect(document.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })
})

describe('BookingRow, причина отмены', () => {
  it('показывает причину отмены в истории посещений', () => {
    renderRow()

    expect(screen.getByText('Барбер заболел').closest('p')).toHaveTextContent(
      'Причина отмены: Барбер заболел'
    )
  })

  it('честно говорит, что причина не указана, у старой отмены', () => {
    renderRow({ cancellationReason: null })

    expect(screen.getByText(/Причина отмены/)).toHaveTextContent(
      'Причина отмены не указана'
    )
  })

  it('не показывает причину у незавершённой записи', () => {
    renderRow({ status: 'Confirmed', cancellationReason: null })

    expect(screen.queryByText(/Причина отмены/)).not.toBeInTheDocument()
  })
})

describe('BookingRow, перенос', () => {
  it('предлагает перенос активной будущей записи с временем в имени', () => {
    const now = Date.parse('2026-11-01T10:00:00Z')

    renderRow({ status: 'Confirmed' }, false, now)

    expect(
      screen.getByRole('button', { name: /^Перенести запись/ })
    ).toBeInTheDocument()
  })

  it('не предлагает перенос прошедшей записи', () => {
    renderRow(
      { status: 'Confirmed' },
      false,
      Date.parse('2026-12-02T10:00:00Z')
    )

    expect(
      screen.queryByRole('button', { name: /^Перенести запись/ })
    ).not.toBeInTheDocument()
  })

  it('не предлагает перенос терминальной записи', () => {
    renderRow({ status: 'Cancelled' })

    expect(
      screen.queryByRole('button', { name: /^Перенести запись/ })
    ).not.toBeInTheDocument()
  })
})
