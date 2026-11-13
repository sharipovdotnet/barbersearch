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
import { stubImageLoading } from '@/test/image'

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
    avatarUrl: null,
  },
  barber: {
    profileId: '9e100000-0000-4000-8000-000000000001',
    displayName: 'Артём Соколов',
    avatarUrl: null,
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

  const result = render(
    <BookingDetail
      booking={booking}
      headingRef={headingRef}
      onReload={onReload}
      onBack={onBack}
      onRequiresSignIn={onRequiresSignIn}
    />
  )

  return {
    headingRef,
    onReload,
    onBack,
    onRequiresSignIn,
    container: result.container,
  }
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

/** `204` для правки цены: тело запроса собирается, чтобы проверить сумму. */
function mockPrice() {
  const calls: { path: string; body: string }[] = []

  server.use(
    http.put('/api/bookings/:bookingId/price', async ({ request }) => {
      calls.push({
        path: new URL(request.url).pathname,
        body: await request.text(),
      })

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

  it('показывает аватары клиента и барбера', async () => {
    const customerAvatarUrl =
      '/api/media/avatars/9e000000-0000-4000-8000-000000000009/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'
    const barberAvatarUrl =
      '/api/media/avatars/9e000000-0000-4000-8000-000000000115/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

    stubImageLoading()

    const { container } = renderDetail(
      makeBooking({
        customer: { ...BASE_BOOKING.customer, avatarUrl: customerAvatarUrl },
        barber: { ...BASE_BOOKING.barber, avatarUrl: barberAvatarUrl },
      })
    )

    await waitFor(() => {
      expect(
        container.querySelectorAll('[data-slot="avatar-image"]')
      ).toHaveLength(2)
    })
    expect(
      container.querySelector(
        `[data-slot="avatar-image"][src="${customerAvatarUrl}"]`
      )
    ).not.toBeNull()
    expect(
      container.querySelector(
        `[data-slot="avatar-image"][src="${barberAvatarUrl}"]`
      )
    ).not.toBeNull()
    expect(screen.getByText('Ирина Волкова')).toBeInTheDocument()
    expect(screen.getByText('Артём Соколов')).toBeInTheDocument()
  })

  it('без фото показывает инициалы клиента и барбера', () => {
    const { container } = renderDetail(makeBooking())

    expect(screen.getByText('ИВ')).toBeInTheDocument()
    expect(screen.getByText('АС')).toBeInTheDocument()
    expect(container.querySelector('[data-slot="avatar-image"]')).toBeNull()
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
          avatarUrl: null,
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

  it('у отменённой записи действий нет: цену тоже не меняют', () => {
    renderDetail(makeBooking({ status: 'Cancelled' }))

    expect(
      screen.queryByRole('button', { name: 'Отменить бронь' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отметить неявку' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Изменить цену визита' })
    ).not.toBeInTheDocument()
  })

  it('у завершённой записи остаётся правка цены', () => {
    renderDetail(makeBooking({ status: 'Completed' }))

    expect(
      screen.getByRole('button', { name: 'Изменить цену визита' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отменить бронь' })
    ).not.toBeInTheDocument()
  })

  it('подсказка секции «Действия» не обещает только отмену и неявку', () => {
    renderDetail(makeBooking({ status: 'Completed' }))

    expect(
      screen.getByText(
        'Цену визита можно изменить в любом статусе, кроме отмены и неявки. Отмена и отметка неявки необратимы; заметку барбера администратор не меняет.'
      )
    ).toBeInTheDocument()
    expect(
      screen.queryByText(
        'Доступны только отмена и отметка неявки. Заметку барбера администратор не меняет.'
      )
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

  it('правка цены отправляет PUT, объявляет результат и перечитывает карточку', async () => {
    const user = userEvent.setup()
    const calls = mockPrice()
    const { onReload } = renderDetail(makeBooking({ status: 'Completed' }))

    await user.click(
      screen.getByRole('button', { name: 'Изменить цену визита' })
    )

    const dialog = screen.getByRole('dialog')
    const input = within(dialog).getByLabelText('Цена, ₽')

    await user.clear(input)
    await user.type(input, '2500')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Цена визита обновлена.'
    )
    expect(calls).toStrictEqual([
      {
        path: `/api/bookings/${BASE_BOOKING.id}/price`,
        body: JSON.stringify({ price: 2500 }),
      },
    ])
    expect(onReload).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('отказ по статусу показывается в диалоге по-русски и не перечитывает карточку', async () => {
    const user = userEvent.setup()

    server.use(
      http.put('/api/bookings/:bookingId/price', () =>
        HttpResponse.json(
          problemDetails(409, {
            detail: 'The price of a booking in status Cancelled cannot be changed.',
            errorCodes: ['booking.price.status'],
          }),
          { status: 409 }
        )
      )
    )

    const { onReload } = renderDetail(makeBooking({ status: 'Confirmed' }))

    await user.click(
      screen.getByRole('button', { name: 'Изменить цену визита' })
    )

    const dialog = screen.getByRole('dialog')
    const input = within(dialog).getByLabelText('Цена, ₽')

    await user.clear(input)
    await user.type(input, '2500')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    expect(
      await within(screen.getByRole('dialog')).findByRole('alert')
    ).toHaveTextContent('Цену нельзя изменить у отменённой записи или у неявки.')
    expect(onReload).not.toHaveBeenCalled()
  })
})
