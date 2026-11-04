import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { BarberSchedulePage } from '@/pages/barber-schedule-page'
import type { BarberSchedule } from '@/lib/barber-schedule'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const SCHEDULE_URL = `/api/barber-profiles/${PROFILE_ID}/schedule`

const BARBER = {
  id: '33333333-3333-4333-8333-333333333333',
  email: 'barber@barbersearch.test',
  role: 'Barber',
  barberProfileId: PROFILE_ID,
  isEmailVerified: true,
} as const

const WEEK = [
  ['Monday', false, '10:00:00', '20:00:00'],
  ['Tuesday', true, '00:00:00', '00:00:00'],
  ['Wednesday', true, '00:00:00', '00:00:00'],
  ['Thursday', true, '00:00:00', '00:00:00'],
  ['Friday', true, '00:00:00', '00:00:00'],
  ['Saturday', true, '00:00:00', '00:00:00'],
  ['Sunday', true, '00:00:00', '00:00:00'],
] as const

const SCHEDULE: BarberSchedule = {
  barberProfileId: PROFILE_ID,
  salons: [
    {
      salonId: SALON_ID,
      name: 'Барбершоп на Ленина',
      city: 'Екатеринбург',
      timeZoneId: 'Asia/Yekaterinburg',
      isPrimary: true,
      weekly: WEEK.map(([dayOfWeek, isDayOff, startTime, endTime]) => ({
        dayOfWeek,
        isDayOff,
        startTime,
        endTime,
      })),
      exceptions: [
        {
          date: '2026-11-15',
          isDayOff: true,
          startTime: '00:00:00',
          endTime: '00:00:00',
          reason: 'Личные дела',
        },
      ],
    },
  ],
  timeOffs: [
    {
      id: '44444444-4444-4444-8444-444444444444',
      startAt: '2026-11-30T19:00:00+00:00',
      endAt: '2026-12-02T19:00:00+00:00',
      reason: 'Отпуск',
    },
  ],
}

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user']
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => {},
    signOut: async () => {},
    retrySession: () => {},
  }
}

function renderPage(
  status: SessionContextValue['status'] = 'authenticated',
  user: SessionContextValue['user'] = BARBER
) {
  return render(
    <SessionContext.Provider value={sessionValue(status, user)}>
      <BarberSchedulePage />
    </SessionContext.Provider>
  )
}

function mockSchedule() {
  const requests: string[] = []

  server.use(
    http.get(SCHEDULE_URL, ({ request }) => {
      requests.push(request.url)

      return HttpResponse.json(SCHEDULE)
    })
  )

  return requests
}

describe('BarberSchedulePage', () => {
  it('аноним видит гейт входа, а не форму', () => {
    renderPage('anonymous', null)

    expect(
      screen.getByText('Войдите, чтобы настроить расписание')
    ).toBeInTheDocument()
    expect(screen.queryByText('Недельное расписание')).not.toBeInTheDocument()
  })

  it('клиент видит объяснение роли', () => {
    renderPage('authenticated', {
      ...BARBER,
      role: 'Customer',
      barberProfileId: null,
    })

    expect(screen.getByText('Это кабинет барбера')).toBeInTheDocument()
  })

  it('показывает неделю, исключения, отпуска и вкладки', async () => {
    mockSchedule()

    renderPage()

    expect(
      await screen.findByRole('heading', { name: 'Расписание', level: 1 })
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Записи' })).toHaveAttribute(
      'href',
      '/barber'
    )
    expect(screen.getByRole('link', { name: 'Расписание' })).toHaveAttribute(
      'href',
      '/barber/schedule'
    )
    expect(screen.getByText('Недельное расписание')).toBeInTheDocument()
    expect(screen.getByText('Исключения на даты')).toBeInTheDocument()
    expect(screen.getByText('Отпуска и нерабочие периоды')).toBeInTheDocument()
    expect(screen.getByText(/Личные дела/)).toBeInTheDocument()
    expect(screen.getByText('Отпуск')).toBeInTheDocument()
  })

  it('сохраняет неделю одним PUT с семью днями', async () => {
    mockSchedule()
    let body: unknown = null

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/working-schedules`,
        async ({ request }) => {
          body = await request.json()

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    renderPage()
    await screen.findByText('Недельное расписание')

    fireEvent.change(screen.getByLabelText('Окончание работы, Понедельник'), {
      target: { value: '18:00' },
    })

    await userEvent.click(
      screen.getByRole('button', { name: 'Сохранить расписание' })
    )

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    const payload = body as { days: { startTime: string; endTime: string }[] }
    expect(payload.days).toHaveLength(7)
    expect(payload.days[0]).toMatchObject({
      dayOfWeek: 'Monday',
      isDayOff: false,
      startTime: '10:00:00',
      endTime: '18:00:00',
    })
    expect(await screen.findByText('Расписание сохранено.')).toBeInTheDocument()
  })

  it('добавляет исключение на выбранную дату', async () => {
    mockSchedule()
    let url = ''
    let body: unknown = null

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-20`,
        async ({ request }) => {
          url = request.url
          body = await request.json()

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    renderPage()
    await screen.findByText('Исключения на даты')

    fireEvent.change(screen.getByLabelText('Дата'), {
      target: { value: '2026-11-20' },
    })
    await userEvent.click(
      screen.getByRole('button', { name: 'Добавить исключение' })
    )

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(new URL(url).pathname).toBe(
      `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-20`
    )
    expect(body).toStrictEqual({
      isDayOff: true,
      startTime: '00:00:00',
      endTime: '00:00:00',
      reason: null,
    })
  })

  it('не даёт сохранить день с концом раньше начала', async () => {
    mockSchedule()

    renderPage()
    await screen.findByText('Недельное расписание')

    fireEvent.change(screen.getByLabelText('Окончание работы, Понедельник'), {
      target: { value: '09:00' },
    })

    expect(
      screen.getAllByText('Конец должен быть позже начала.').length
    ).toBeGreaterThan(0)
    expect(
      screen.getByRole('button', { name: 'Сохранить расписание' })
    ).toBeDisabled()
  })

  it('показывает ошибку сервера на сохранении недели', async () => {
    mockSchedule()

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/working-schedules`,
        () =>
          HttpResponse.json(
            problemDetails(400, {
              errorCodes: ['barberSalon.replaceSchedule.timeRange'],
            }),
            { status: 400 }
          )
      )
    )

    renderPage()
    await screen.findByText('Недельное расписание')

    fireEvent.change(screen.getByLabelText('Окончание работы, Понедельник'), {
      target: { value: '18:00' },
    })
    await userEvent.click(
      screen.getByRole('button', { name: 'Сохранить расписание' })
    )

    expect(
      await screen.findByText(
        'Конец рабочего дня должен быть позже начала.'
      )
    ).toBeInTheDocument()
  })

  it('не даёт добавить отпуск с концом раньше начала', async () => {
    mockSchedule()

    renderPage()
    await screen.findByText('Отпуска и нерабочие периоды')

    fireEvent.change(screen.getByLabelText('С'), {
      target: { value: '2026-12-03' },
    })
    fireEvent.change(screen.getByLabelText('По'), {
      target: { value: '2026-12-01' },
    })

    expect(
      screen.getByText('Конец периода должен быть не раньше начала.')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Добавить отпуск' })
    ).toBeDisabled()
  })

  it('добавляет отпуск и отправляет конец следующего дня в UTC', async () => {
    mockSchedule()
    let body: unknown = null

    server.use(
      http.post(
        `/api/barber-profiles/${PROFILE_ID}/time-offs`,
        async ({ request }) => {
          body = await request.json()

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    renderPage()
    await screen.findByText('Отпуска и нерабочие периоды')

    fireEvent.change(screen.getByLabelText('С'), {
      target: { value: '2026-12-01' },
    })
    fireEvent.change(screen.getByLabelText('По'), {
      target: { value: '2026-12-03' },
    })

    await userEvent.click(
      screen.getByRole('button', { name: 'Добавить отпуск' })
    )

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(body).toStrictEqual({
      startAt: '2026-11-30T19:00:00.000Z',
      endAt: '2026-12-03T19:00:00.000Z',
      reason: null,
    })
  })
})
