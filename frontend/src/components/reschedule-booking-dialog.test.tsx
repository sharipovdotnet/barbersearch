import { useState } from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import {
  RescheduleBookingDialog,
  type RescheduleTarget,
} from '@/components/reschedule-booking-dialog'
import { ApiError } from '@/lib/api'
import type { BarberSlot, BarberSlots } from '@/lib/barber-slots'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Диалог переноса: состояния сетки, выбор слота, отправка и ошибки по кодам.
 *
 * Запрос выполняет родитель (`onRescheduled`), поэтому 409 приходит сюда
 * отклонённым промисом — диалог обязан показать текст и перезапросить слоты.
 */

const TARGET: RescheduleTarget = {
  bookingId: 'booking-1',
  startAt: '2026-11-06T09:00:00Z',
  endAt: '2026-11-06T09:30:00Z',
  timeZoneId: 'Europe/Moscow',
  serviceName: 'Стрижка',
}

const PATH = '/api/bookings/booking-1/reschedule-slots'

function slots(
  date: string,
  options: { isWorkingDay?: boolean; count?: number } = {}
): BarberSlots {
  const count = options.count ?? 2

  return {
    barberProfileId: '22222222-2222-4222-8222-222222222222',
    salonId: '33333333-3333-4333-8333-333333333333',
    barberServiceId: '44444444-4444-4444-8444-444444444444',
    hairServiceId: null,
    date,
    timeZoneId: 'Europe/Moscow',
    dayStart: `${date}T00:00:00Z`,
    dayEnd: `${date}T21:00:00Z`,
    durationMinutes: 30,
    isWorkingDay: options.isWorkingDay ?? true,
    slots: Array.from({ length: count }, (_, index) => {
      const start = new Date(`${date}T07:00:00Z`).getTime() + index * 30 * 60_000

      return {
        startAt: new Date(start).toISOString(),
        endAt: new Date(start + 30 * 60_000).toISOString(),
      }
    }),
  }
}

function serveSlots(result: (date: string) => BarberSlots): string[] {
  const dates: string[] = []

  server.use(
    http.get(PATH, ({ request }) => {
      const date = new URL(request.url).searchParams.get('date') ?? ''

      dates.push(date)

      return HttpResponse.json(result(date))
    })
  )

  return dates
}

function renderDialog(
  props: Partial<{
    variant: 'customer' | 'barber'
    onClose: () => void
    onRescheduled: (slot: BarberSlot) => Promise<void>
  }> = {}
) {
  return render(
    <RescheduleBookingDialog
      target={TARGET}
      variant={props.variant ?? 'customer'}
      onClose={props.onClose ?? vi.fn()}
      onRescheduled={props.onRescheduled ?? vi.fn(async () => undefined)}
    />
  )
}

describe('RescheduleBookingDialog: сетка', () => {
  it('обычный dialog, скелетон, затем количество слотов в role=status', async () => {
    const gate: { release: (() => void) | null } = { release: null }

    server.use(
      http.get(PATH, () => {
        return new Promise<Response>((resolve) => {
          gate.release = () => resolve(HttpResponse.json(slots('2026-11-06')))
        })
      })
    )

    renderDialog()

    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(
      screen.getByText('Длительность и цена не изменятся.')
    ).toBeInTheDocument()
    // У клиента нет строки про письмо — это правда только для барбера.
    expect(
      screen.queryByText(
        'Клиент увидит новое время в кабинете. Письмо не отправляется.'
      )
    ).not.toBeInTheDocument()

    expect(screen.getByRole('status')).toHaveTextContent(
      'Загружаем свободное время…'
    )

    await waitFor(() => {
      expect(gate.release).not.toBeNull()
    })
    gate.release?.()

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('Свободно 2 слота')
    })
    expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
  })

  it('нерабочий день и пустой день говорят честно', async () => {
    serveSlots(() => slots('2026-11-06', { isWorkingDay: false }))
    renderDialog()

    await waitFor(() => {
      expect(
        screen.getAllByText('В этот день барбер не работает.').length
      ).toBeGreaterThan(0)
    })
    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
  })

  it('свободного времени нет', async () => {
    serveSlots(() => slots('2026-11-06', { count: 0 }))
    renderDialog()

    await waitFor(() => {
      expect(
        screen.getAllByText('Свободного времени на этот день нет.').length
      ).toBeGreaterThan(0)
    })
  })

  it('ошибка загрузки — role=alert и «Повторить»', async () => {
    let calls = 0

    server.use(
      http.get(PATH, () => {
        calls += 1

        if (calls === 1) {
          return HttpResponse.json(problemDetails(500), { status: 500 })
        }

        return HttpResponse.json(slots('2026-11-06'))
      })
    )

    renderDialog()

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })

    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
    })
    expect(calls).toBe(2)
  })

  it('смена даты сбрасывает выбор и перезапрашивает сетку', async () => {
    const dates = serveSlots((date) => slots(date))
    const user = userEvent.setup()

    renderDialog()

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
    })

    await user.click(screen.getByText('10:00'))
    expect(screen.getByRole('radio', { name: '10:00' })).toBeChecked()

    fireEvent.change(screen.getByLabelText('Новая дата'), {
      target: { value: '2026-11-07' },
    })

    await waitFor(() => {
      expect(dates).toStrictEqual(['2026-11-06', '2026-11-07'])
    })

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
    })
    expect(screen.getByRole('radio', { name: '10:00' })).not.toBeChecked()
  })
})

describe('RescheduleBookingDialog: отправка', () => {
  it('выбранный слот уходит в onRescheduled, затем onClose', async () => {
    serveSlots(() => slots('2026-11-06'))

    const onRescheduled = vi.fn(async () => undefined)
    const onClose = vi.fn()
    const user = userEvent.setup()

    renderDialog({ onRescheduled, onClose })

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
    })

    const submit = screen.getByRole('button', { name: 'Перенести визит' })

    expect(submit).toBeDisabled()

    await user.click(screen.getByText('10:00'))
    await user.click(submit)

    await waitFor(() => {
      expect(onRescheduled).toHaveBeenCalledWith({
        startAt: '2026-11-06T07:00:00.000Z',
        endAt: '2026-11-06T07:30:00.000Z',
      })
    })
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('409 overlap — текст, перезапрос слотов и сброшенный выбор', async () => {
    const dates = serveSlots((date) => slots(date))
    const onRescheduled = vi.fn(async () => {
      throw new ApiError(
        409,
        problemDetails(409, { errorCodes: ['booking.reschedule.overlap'] }),
        'Overlap.'
      )
    })
    const user = userEvent.setup()

    renderDialog({ onRescheduled })

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
    })

    await user.click(screen.getByText('10:00'))
    await user.click(screen.getByRole('button', { name: 'Перенести визит' }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Это время уже занято. Выберите другое.'
      )
    })

    await waitFor(() => {
      expect(dates).toHaveLength(2)
    })

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Перенести визит' })
      ).toBeDisabled()
    })
    expect(screen.getByRole('radio', { name: '10:00' })).not.toBeChecked()
  })

  it('409 status — без перезапроса слотов', async () => {
    const dates = serveSlots((date) => slots(date))
    const onRescheduled = vi.fn(async () => {
      throw new ApiError(
        409,
        problemDetails(409, { errorCodes: ['booking.reschedule.status'] }),
        'Started.'
      )
    })
    const user = userEvent.setup()

    renderDialog({ onRescheduled })

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: '10:00' })).toBeInTheDocument()
    })

    await user.click(screen.getByText('10:00'))
    await user.click(screen.getByRole('button', { name: 'Перенести визит' }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        'Запись уже нельзя перенести — обновите страницу.'
      )
    })
    expect(dates).toHaveLength(1)
  })

  it('строка про письмо — только у барбера', async () => {
    serveSlots(() => slots('2026-11-06'))
    renderDialog({ variant: 'barber' })

    expect(
      screen.getByText(
        'Клиент увидит новое время в кабинете. Письмо не отправляется.'
      )
    ).toBeInTheDocument()
  })
})

describe('RescheduleBookingDialog: клавиатура', () => {
  it('Escape закрывает диалог и возвращает фокус на кнопку строки', async () => {
    serveSlots(() => slots('2026-11-06'))

    function Harness() {
      const [open, setOpen] = useState(false)

      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Открыть перенос
          </button>
          <RescheduleBookingDialog
            target={open ? TARGET : null}
            variant="customer"
            onClose={() => setOpen(false)}
            onRescheduled={vi.fn(async () => undefined)}
          />
        </>
      )
    }

    const user = userEvent.setup()

    render(<Harness />)

    const trigger = screen.getByRole('button', { name: 'Открыть перенос' })

    await user.click(trigger)

    const dialog = screen.getByRole('dialog')

    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Новая дата')).toBeInTheDocument()

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
    await waitFor(() => {
      expect(trigger).toHaveFocus()
    })
  })
})
