import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { BookingRow } from '@/components/account/booking-row'
import type { BookingView } from '@/components/account/booking-view'

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
  },
  barberName: 'Барбер Номер 1',
  serviceName: 'Стрижка',
  salonName: 'Салон',
  salonAddress: 'ул. Тестовая, 1',
  salonCity: 'Берлин',
  timeZoneId: 'Europe/Berlin',
  durationMinutes: 30,
  isResolved: true,
}

function renderRow(overrides: Partial<BookingView['booking']> = {}) {
  return render(
    <ul>
      <BookingRow
        view={{ ...VIEW, booking: { ...VIEW.booking, ...overrides } }}
        isReviewed={false}
        onRequestCancel={vi.fn()}
        onRequestReview={vi.fn()}
      />
    </ul>
  )
}

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
