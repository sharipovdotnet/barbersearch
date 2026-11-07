import { render, screen, waitFor } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { BarberBookingDialog } from '@/components/barber/barber-booking-dialog'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'

const CLIENT = {
  id: '22222222-2222-4222-8222-222222222222',
  firstName: 'Иван',
  lastName: 'Петров',
  email: 'client@example.com',
  phoneNumber: '+7 999 000-00-00',
}

const SERVICE = {
  barberServiceId: '33333333-3333-4333-8333-333333333333',
  hairServiceId: '44444444-4444-4444-8444-444444444444',
  name: 'Стрижка',
  category: 'Haircut' as const,
  defaultDurationMinutes: 30,
  price: 2000,
  durationMinutes: 30,
  isActive: true,
}

const SALON = {
  id: '55555555-5555-4555-8555-555555555555',
  salonId: '66666666-6666-4666-8666-666666666666',
  name: 'Салон на Тверской',
  address: 'Тверская, 1',
  city: 'Москва',
  country: 'Россия',
  timeZoneId: 'Europe/Moscow',
  kind: 'Salon',
  isPrimary: true,
  workingSchedules: [],
}

const SLOT = {
  startAt: '2026-11-05T07:00:00Z',
  endAt: '2026-11-05T07:30:00Z',
}

/** Слот по UTC-часам: Москва — UTC+3, поэтому 07:00Z показывается как «10:00». */
function slot(hours: number, minutes = 0): { startAt: string; endAt: string } {
  const start = new Date(Date.UTC(2026, 10, 5, hours, minutes, 0))

  return {
    startAt: start.toISOString(),
    endAt: new Date(start.getTime() + 30 * 60_000).toISOString(),
  }
}

const SLOTS = {
  barberProfileId: PROFILE_ID,
  salonId: SALON.salonId,
  barberServiceId: SERVICE.barberServiceId,
  hairServiceId: SERVICE.hairServiceId,
  date: '2026-11-05',
  timeZoneId: SALON.timeZoneId,
  dayStart: '2026-11-05T00:00:00Z',
  dayEnd: '2026-11-05T23:59:59Z',
  durationMinutes: 30,
  isWorkingDay: true,
  slots: [SLOT],
}

function useFormHandlers() {
  server.use(
    http.get('/api/barber/booking-options', () =>
      HttpResponse.json({ services: [SERVICE], salons: [SALON] })
    ),
    http.get('/api/barber/clients', () => HttpResponse.json([CLIENT])),
    http.get('/api/barber/slots', () => HttpResponse.json(SLOTS))
  )
}

async function fillForm(user: UserEvent) {
  await user.click(screen.getByRole('button', { name: 'Записать клиента' }))
  await screen.findByRole('dialog')

  const search = await screen.findByRole('searchbox', { name: 'Клиент' })
  await user.type(search, 'client@example.com')

  const clientButton = await screen.findByRole('button', {
    name: /Иван Петров/,
  })
  await user.click(clientButton)

  await user.selectOptions(
    await screen.findByRole('combobox', { name: 'Услуга' }),
    SERVICE.barberServiceId
  )

  const slot = await screen.findByRole('radio', { name: '10:00' })
  await user.click(slot)
}

describe('BarberBookingDialog', () => {
  it('записывает выбранного клиента, шлёт CSRF и сообщает имя', async () => {
    useFormHandlers()

    const captured = {
      csrf: null as string | null,
      body: '',
    }

    server.use(
      http.post('/api/barber/bookings', async ({ request }) => {
        captured.csrf = request.headers.get('x-csrf-token')
        captured.body = await request.text()

        return HttpResponse.json(
          { id: '77777777-7777-4777-8777-777777777777' },
          { status: 201 }
        )
      })
    )

    const onCreated = vi.fn()
    const user = userEvent.setup()

    render(
      <BarberBookingDialog
        barberProfileId={PROFILE_ID}
        onCreated={onCreated}
      />
    )

    await fillForm(user)

    const submit = screen.getByRole('button', { name: 'Записать' })
    await waitFor(() => expect(submit).toBeEnabled())
    await user.click(submit)

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith('Иван Петров'))
    expect(captured.csrf).toBe('csrf-token')

    const body = JSON.parse(captured.body) as Record<string, unknown>

    expect(body).toMatchObject({
      customerId: CLIENT.id,
      barberServiceId: SERVICE.barberServiceId,
      salonId: SALON.salonId,
      startAt: SLOT.startAt,
      endAt: SLOT.endAt,
      customerNote: null,
    })
    expect(body.idempotencyKey).toEqual(expect.any(String))
  })

  it('записывает нового клиента без аккаунта по имени и телефону', async () => {
    useFormHandlers()

    const captured = {
      csrf: null as string | null,
      body: '',
    }

    server.use(
      http.post('/api/barber/bookings', async ({ request }) => {
        captured.csrf = request.headers.get('x-csrf-token')
        captured.body = await request.text()

        return HttpResponse.json(
          { id: '77777777-7777-4777-8777-777777777777' },
          { status: 201 }
        )
      })
    )

    const onCreated = vi.fn()
    const user = userEvent.setup()

    render(
      <BarberBookingDialog
        barberProfileId={PROFILE_ID}
        onCreated={onCreated}
      />
    )

    await user.click(screen.getByRole('button', { name: 'Записать клиента' }))
    await screen.findByRole('dialog')

    await user.click(
      await screen.findByRole('radio', { name: 'Новый клиент' })
    )
    await user.type(screen.getByLabelText(/Имя/), 'Анна')
    await user.type(screen.getByLabelText(/Фамилия/), 'Смирнова')
    await user.type(
      screen.getByLabelText(/Телефон/),
      '+7 999 111-22-33'
    )

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Услуга' }),
      SERVICE.barberServiceId
    )

    await user.click(await screen.findByRole('radio', { name: '10:00' }))
    await user.click(screen.getByRole('button', { name: 'Записать' }))

    await waitFor(() =>
      expect(onCreated).toHaveBeenCalledWith('Анна Смирнова')
    )
    expect(captured.csrf).toBe('csrf-token')

    const body = JSON.parse(captured.body) as Record<string, unknown>

    expect(body).toMatchObject({
      customerId: null,
      guestFirstName: 'Анна',
      guestLastName: 'Смирнова',
      guestPhone: '+7 999 111-22-33',
      barberServiceId: SERVICE.barberServiceId,
      salonId: SALON.salonId,
      startAt: SLOT.startAt,
      endAt: SLOT.endAt,
      customerNote: null,
    })
    expect(body.idempotencyKey).toEqual(expect.any(String))
  })

  it('на пересечение времени показывает русский текст и перечитывает слоты', async () => {
    let slotsRequests = 0

    server.use(
      http.get('/api/barber/booking-options', () =>
        HttpResponse.json({ services: [SERVICE], salons: [SALON] })
      ),
      http.get('/api/barber/clients', () => HttpResponse.json([CLIENT])),
      http.get('/api/barber/slots', () => {
        slotsRequests += 1

        return HttpResponse.json(SLOTS)
      }),
      http.post('/api/barber/bookings', () =>
        HttpResponse.json(
          problemDetails(409, { errorCodes: ['booking.add.overlap'] }),
          { status: 409 }
        )
      )
    )

    const onCreated = vi.fn()
    const user = userEvent.setup()

    render(
      <BarberBookingDialog
        barberProfileId={PROFILE_ID}
        onCreated={onCreated}
      />
    )

    await fillForm(user)

    const before = slotsRequests
    await user.click(screen.getByRole('button', { name: 'Записать' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Это время только что заняли. Выберите другое.'
    )

    await waitFor(() => expect(slotsRequests).toBeGreaterThan(before))
    expect(onCreated).not.toHaveBeenCalled()
  })
})

describe('BarberBookingDialog: предвыбор времени из окна дня', () => {
  const DAY_SLOTS = {
    ...SLOTS,
    slots: [slot(7, 0), slot(7, 30), slot(8, 0)],
  }

  function useSlotHandlers() {
    server.use(
      http.get('/api/barber/booking-options', () =>
        HttpResponse.json({ services: [SERVICE], salons: [SALON] })
      ),
      http.get('/api/barber/clients', () => HttpResponse.json([CLIENT])),
      http.get('/api/barber/slots', () => HttpResponse.json(DAY_SLOTS))
    )
  }

  async function openWithPreferred(preferredStartAt?: string) {
    const user = userEvent.setup()

    render(
      <BarberBookingDialog
        barberProfileId={PROFILE_ID}
        preferredStartAt={preferredStartAt}
        onCreated={vi.fn()}
      />
    )

    await user.click(screen.getByRole('button', { name: 'Записать клиента' }))
    await screen.findByRole('dialog')

    return user
  }

  it('выбирает слот, совпадающий с началом окна', async () => {
    useSlotHandlers()

    const user = await openWithPreferred(slot(7, 30).startAt)

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Услуга' }),
      SERVICE.barberServiceId
    )

    expect(await screen.findByRole('radio', { name: '10:30' })).toBeChecked()
    expect(screen.getByRole('radio', { name: '10:00' })).not.toBeChecked()
  })

  it('без точного совпадения берёт ближайший слот не раньше окна', async () => {
    useSlotHandlers()

    // 07:40Z между 10:30 и 11:00 по Москве: ближайший будущий — 11:00.
    const user = await openWithPreferred(
      new Date(Date.UTC(2026, 10, 5, 7, 40, 0)).toISOString()
    )

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Услуга' }),
      SERVICE.barberServiceId
    )

    expect(await screen.findByRole('radio', { name: '11:00' })).toBeChecked()
  })

  it('без желаемого времени ничего не выбрано', async () => {
    useSlotHandlers()

    const user = await openWithPreferred()

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Услуга' }),
      SERVICE.barberServiceId
    )

    await screen.findByRole('radio', { name: '10:00' })
    expect(screen.getByRole('radio', { name: '10:00' })).not.toBeChecked()
    expect(screen.getByRole('radio', { name: '11:00' })).not.toBeChecked()
  })

  it('смена услуги пересчитывает предвыбор на новой сетке', async () => {
    const SECOND_SERVICE = {
      ...SERVICE,
      barberServiceId: '88888888-8888-4888-8888-888888888888',
      hairServiceId: '99999999-9999-4999-8999-999999999999',
      name: 'Борода',
    }

    server.use(
      http.get('/api/barber/booking-options', () =>
        HttpResponse.json({ services: [SERVICE, SECOND_SERVICE], salons: [SALON] })
      ),
      http.get('/api/barber/clients', () => HttpResponse.json([CLIENT])),
      http.get('/api/barber/slots', ({ request }) => {
        const hairServiceId = new URL(request.url).searchParams.get(
          'hairServiceId'
        )

        return HttpResponse.json(
          hairServiceId === SECOND_SERVICE.hairServiceId
            ? { ...DAY_SLOTS, slots: [slot(7, 45), slot(8, 15)] }
            : DAY_SLOTS
        )
      })
    )

    const user = await openWithPreferred(slot(7, 30).startAt)

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Услуга' }),
      SERVICE.barberServiceId
    )
    expect(await screen.findByRole('radio', { name: '10:30' })).toBeChecked()

    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Услуга' }),
      SECOND_SERVICE.barberServiceId
    )

    expect(await screen.findByRole('radio', { name: '10:45' })).toBeChecked()
  })
})
