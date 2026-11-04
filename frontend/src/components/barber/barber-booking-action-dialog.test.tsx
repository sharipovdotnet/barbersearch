import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { BarberBookingActionDialog } from '@/components/barber/barber-booking-action-dialog'
import type { BarberBooking } from '@/lib/barber-bookings'

const BOOKING: BarberBooking = {
  id: '77777777-7777-4777-8777-777777777777',
  status: 'Confirmed',
  startAt: '2026-12-01T10:00:00Z',
  endAt: '2026-12-01T10:30:00Z',
  price: 45,
  customerNote: null,
  barberNote: null,
  cancellationReason: null,
  customer: { displayName: 'Иван Петров', phoneNumber: null },
  service: { name: 'Стрижка' },
  salon: {
    name: 'Салон',
    address: 'ул. Тестовая, 1',
    city: 'Берлин',
    timeZoneId: 'Europe/Berlin',
  },
}

function renderDialog(onConfirm = vi.fn(async () => {})) {
  render(
    <BarberBookingActionDialog
      target={{ booking: BOOKING, kind: 'cancel' }}
      onClose={vi.fn()}
      onConfirm={onConfirm}
      onDone={vi.fn()}
      onReload={vi.fn()}
    />
  )

  return { onConfirm, dialog: screen.getByRole('alertdialog') }
}

describe('BarberBookingActionDialog, отмена', () => {
  it('без причины не отправляет отмену и показывает русскую ошибку', async () => {
    const user = userEvent.setup()
    const { onConfirm, dialog } = renderDialog()

    await user.click(
      within(dialog).getByRole('button', { name: 'Отменить запись' })
    )

    expect(
      within(dialog).getByText('Укажите причину отмены.')
    ).toBeInTheDocument()
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('отправляет причину вместе с отменой', async () => {
    const user = userEvent.setup()
    const { onConfirm, dialog } = renderDialog()

    await user.type(
      within(dialog).getByLabelText('Причина отмены'),
      'Барбер заболел'
    )
    await user.click(
      within(dialog).getByRole('button', { name: 'Отменить запись' })
    )

    expect(onConfirm).toHaveBeenCalledWith(
      'cancel',
      BOOKING.id,
      'Барбер заболел'
    )
  })

  it('не показывает поле причины у неявки', () => {
    render(
      <BarberBookingActionDialog
        target={{ booking: BOOKING, kind: 'noShow' }}
        onClose={vi.fn()}
        onConfirm={vi.fn(async () => {})}
        onDone={vi.fn()}
        onReload={vi.fn()}
      />
    )

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).queryByLabelText('Причина отмены')
    ).not.toBeInTheDocument()
  })
})
