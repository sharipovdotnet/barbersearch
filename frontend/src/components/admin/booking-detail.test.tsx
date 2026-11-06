import { createRef } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BookingDetail } from '@/components/admin/booking-detail'
import type { AdminBooking } from '@/lib/admin-bookings'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Карточка брони: время в поясе салона, действия по статусу и подтверждения.
 *
 * Сеть — только MSW на границе HTTP. Русская копия сравнивается целиком:
 * тексты подтверждений и ошибок — часть обещания экрана, а не оформление.
 */

const BASE_BOOKING: AdminBooking = {
  id: '9e900000-0000-4000-8000-000000000001',
  status: 'Pending',
  startAt: '2026-09-12T20:30:00+00:00',
  endAt: '2026-09-12T21:00:00+00:00',
  price: 30,
  customerNote: 'Позвоните за час',
  barberNote: 'Клиент предупреждён',
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

function makeBooking(overrides: Partial<AdminBooking> = {}): AdminBooking {
  return { ...BASE_BOOKING, ...overrides }
}

function renderDetail(booking: AdminBooking) {
  const headingRef = createRef<HTMLHeadingElement>()
  const onReload = vi.fn()
  const onBack = vi.fn()
  const onRequiresSignIn = vi.fn()

  render(
    <BookingDetail
      booking={booking}
      headingRef={headingRef}
      onReload={onReload}
      onBack={onBack}
      onRequiresSignIn={onRequiresSignIn}
    />
  )

  return { headingRef, onReload, onBack, onRequiresSignIn }
}

/** `204` для отмены или неявки: так отвечает реальный API. */
function mockAction(path: 'cancellation' | 'no-show') {
  const calls: string[] = []

  server.use(
    http.post(`/api/bookings/:bookingId/${path}`, ({ request }) => {
      calls.push(new URL(request.url).pathname)

      return new HttpResponse(null, { status: 204 })
    })
  )

  return calls
}

describe('BookingDetail', () => {
  it('показывает время в поясе салона, клиента, барбера, услугу и салон', () => {
    renderDetail(makeBooking())

    expect(
      screen.getByRole('heading', { name: 'Бронь', level: 2 })
    ).toBeInTheDocument()
    expect(screen.getByText('Ждёт подтверждения')).toBeInTheDocument()
    // 20:30 UTC — это 23:30 в Москве; в поясе браузера теста было бы другое время.
    expect(screen.getAllByText(/23:30/).length).toBeGreaterThan(0)
    expect(screen.queryByText(/01:30/)).not.toBeInTheDocument()
    expect(screen.getByText('Ирина Волкова')).toBeInTheDocument()
    expect(
      screen.getByText('e2e-bookings@barbersearch.test')
    ).toBeInTheDocument()
    expect(screen.getByText('+70000000009')).toBeInTheDocument()
    expect(screen.getByText('Артём Соколов')).toBeInTheDocument()
    expect(screen.getByText('Стрижка')).toBeInTheDocument()
    expect(screen.getByText('BarberSearch Тверская')).toBeInTheDocument()
    expect(
      screen.getByText('Время в поясе салона: Europe/Moscow')
    ).toBeInTheDocument()
    expect(screen.getByText('Позвоните за час')).toBeInTheDocument()
    expect(screen.getByText('Клиент предупреждён')).toBeInTheDocument()
    expect(screen.getByText(BASE_BOOKING.id)).toBeInTheDocument()
  })

  it('показывает гостя без аккаунта по имени и телефону', () => {
    renderDetail(
      makeBooking({
        customer: {
          id: null,
          firstName: 'Анна',
          lastName: 'Смирнова',
          email: null,
          phoneNumber: '+79991112233',
        },
      })
    )

    expect(screen.getByText('Анна Смирнова')).toBeInTheDocument()
    expect(screen.getByText('+79991112233')).toBeInTheDocument()
    expect(screen.getByText('Записан без аккаунта')).toBeInTheDocument()
    expect(screen.queryByText(/@/)).not.toBeInTheDocument()
  })

  it('для Pending показывает только отмену', () => {
    renderDetail(makeBooking({ status: 'Pending' }))

    expect(
      screen.getByRole('button', { name: 'Отменить бронь' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отметить неявку' })
    ).not.toBeInTheDocument()
  })

  it('для Confirmed с будущим началом не показывает неявку', () => {
    renderDetail(
      makeBooking({
        status: 'Confirmed',
        startAt: '2099-09-12T20:30:00+00:00',
        endAt: '2099-09-12T21:00:00+00:00',
      })
    )

    expect(
      screen.getByRole('button', { name: 'Отменить бронь' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отметить неявку' })
    ).not.toBeInTheDocument()
  })

  it('для Confirmed с прошедшим началом показывает обе операции', () => {
    renderDetail(
      makeBooking({
        status: 'Confirmed',
        startAt: '2020-09-12T20:30:00+00:00',
        endAt: '2020-09-12T21:00:00+00:00',
      })
    )

    expect(
      screen.getByRole('button', { name: 'Отменить бронь' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Отметить неявку' })
    ).toBeInTheDocument()
  })

  it('для терминального статуса операций нет', () => {
    renderDetail(makeBooking({ status: 'Completed' }))

    expect(
      screen.queryByRole('button', { name: 'Отменить бронь' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отметить неявку' })
    ).not.toBeInTheDocument()
  })

  it('отмена подтверждается alertdialog и после успеха объявляет результат', async () => {
    const user = userEvent.setup()
    const calls = mockAction('cancellation')
    const { onReload, headingRef } = renderDetail(
      makeBooking({ status: 'Confirmed' })
    )

    await user.click(
      screen.getByRole('button', { name: 'Отменить бронь' })
    )

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).getByText(
        'Бронь будет отменена. Клиент и барбер увидят новый статус.'
      )
    ).toBeInTheDocument()
    expect(
      within(dialog).getByRole('button', { name: 'Оставить как есть' })
    ).toBeInTheDocument()

    await user.click(
      within(dialog).getByRole('button', { name: 'Отменить бронь' })
    )

    expect(await screen.findByRole('status')).toHaveTextContent('Бронь отменена.')
    expect(calls).toStrictEqual([
      `/api/bookings/${BASE_BOOKING.id}/cancellation`,
    ])
    expect(onReload).toHaveBeenCalledTimes(1)

    await waitFor(() => {
      expect(headingRef.current).toHaveFocus()
    })
  })

  it('неявка подтверждается и объявляет результат', async () => {
    const user = userEvent.setup()
    const calls = mockAction('no-show')
    const { onReload } = renderDetail(
      makeBooking({
        status: 'Confirmed',
        startAt: '2020-09-12T20:30:00+00:00',
        endAt: '2020-09-12T21:00:00+00:00',
      })
    )

    await user.click(screen.getByRole('button', { name: 'Отметить неявку' }))

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).getByText(
        'Визит будет отмечен как неявка. Отменить это действие нельзя.'
      )
    ).toBeInTheDocument()

    await user.click(
      within(dialog).getByRole('button', { name: 'Отметить неявку' })
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Неявка отмечена.'
    )
    expect(calls).toStrictEqual([`/api/bookings/${BASE_BOOKING.id}/no-show`])
    expect(onReload).toHaveBeenCalledTimes(1)
  })

  it('«Оставить как есть» закрывает диалог без запроса', async () => {
    const user = userEvent.setup()
    const actionCalls = countRequests(
      `/api/bookings/${BASE_BOOKING.id}/cancellation`
    )
    const { onReload } = renderDetail(makeBooking({ status: 'Pending' }))

    await user.click(
      screen.getByRole('button', { name: 'Отменить бронь' })
    )
    await user.click(
      screen.getByRole('button', { name: 'Оставить как есть' })
    )

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(actionCalls()).toBe(0)
    expect(onReload).not.toHaveBeenCalled()
  })

  it('ошибка действия показывается по-русски, без detail и без перезагрузки', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(
        `/api/bookings/:bookingId/cancellation`,
        () =>
          HttpResponse.json(
            problemDetails(409, {
              detail: 'Invalid status.',
              errorCodes: ['booking.cancel.invalidStatus'],
            }),
            { status: 409 }
          )
      )
    )

    const { onReload } = renderDetail(makeBooking({ status: 'Confirmed' }))

    await user.click(
      screen.getByRole('button', { name: 'Отменить бронь' })
    )
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Отменить бронь',
      })
    )

    expect(
      await screen.findByRole('alert')
    ).toHaveTextContent('Бронь уже завершена или отменена.')
    expect(screen.queryByText('Invalid status.')).not.toBeInTheDocument()
    expect(onReload).not.toHaveBeenCalled()
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
  })
})
