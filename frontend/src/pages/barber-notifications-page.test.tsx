import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { BarberNotificationsPage } from '@/pages/barber-notifications-page'
import { notificationSettings, problemDetails, unauthorized } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const UPCOMING_ID = '22222222-2222-4222-8222-222222222222'
const BOOKING_ID = '33333333-3333-4333-8333-333333333333'
const DIGEST_ID = '44444444-4444-4444-8444-444444444444'
const NOTIFICATIONS_URL = '/api/barber/notifications'

const BARBER = {
  id: '55555555-5555-4555-8555-555555555555',
  email: 'barber@barbersearch.test',
  role: 'Barber',
  barberProfileId: PROFILE_ID,
  isEmailVerified: true,
} as const

function upcoming(readAt: string | null = null) {
  return {
    id: UPCOMING_ID,
    kind: 'Upcoming',
    createdAt: '2026-11-07T09:02:00Z',
    readAt,
    bookingId: BOOKING_ID,
    startAt: '2026-11-07T09:00:00Z',
    serviceName: 'стрижка',
    salonName: 'Барбершоп на Ленина',
    salonTimeZoneId: 'Asia/Yekaterinburg',
    bookingsCount: null,
    delivery: 'Sent',
  }
}

function digest(readAt: string | null = null) {
  return {
    id: DIGEST_ID,
    kind: 'Digest',
    createdAt: '2026-11-07T03:00:00Z',
    readAt,
    bookingId: null,
    startAt: '2026-11-07T05:00:00Z',
    serviceName: null,
    salonName: null,
    salonTimeZoneId: 'Asia/Yekaterinburg',
    bookingsCount: 4,
    delivery: 'Skipped',
  }
}

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user']
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => BARBER,
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
      <BarberNotificationsPage />
    </SessionContext.Provider>
  )
}

beforeEach(() => {
  window.history.replaceState(null, '', '/barber/notifications')
})

describe('BarberNotificationsPage, гейты', () => {
  it('аноним видит вход и не делает запросов', () => {
    let requests = 0

    server.use(
      http.get(NOTIFICATIONS_URL, () => {
        requests += 1

        return HttpResponse.json({ items: [], nextCursor: null })
      }),
      http.get('/api/barber/notification-settings', () => {
        requests += 1

        return HttpResponse.json(notificationSettings)
      })
    )

    renderPage('anonymous', null)

    expect(
      screen.getByText('Войдите, чтобы увидеть уведомления')
    ).toBeInTheDocument()
    expect(requests).toBe(0)
  })

  it('клиент видит объяснение роли', () => {
    renderPage('authenticated', {
      ...BARBER,
      role: 'Customer',
      barberProfileId: null,
    })

    expect(screen.getByText('Это кабинет барбера')).toBeInTheDocument()
  })

  it('барбер без профиля видит гейт профиля, а не список', () => {
    renderPage('authenticated', { ...BARBER, barberProfileId: null })

    expect(screen.getByText('Профиль ещё не создан')).toBeInTheDocument()
  })
})

describe('BarberNotificationsPage, список', () => {
  it('показывает пустое состояние и выключенную кнопку «Прочитать все»', async () => {
    renderPage()

    expect(await screen.findByText('Уведомлений пока нет.')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Прочитать все' })
    ).toBeDisabled()
  })

  it('показывает ошибку загрузки с повтором', async () => {
    server.use(
      http.get(NOTIFICATIONS_URL, () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderPage()

    expect(
      await screen.findByText('Не удалось загрузить уведомления.')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Повторить' })
    ).toBeInTheDocument()
  })

  it('401 на списке показывает гейт входа', async () => {
    server.use(http.get(NOTIFICATIONS_URL, () => unauthorized()))

    renderPage()

    expect(await screen.findByText('Сессия истекла')).toBeInTheDocument()
  })

  it('рисует строки Upcoming и Digest с временем в поясе салона', async () => {
    server.use(
      http.get(NOTIFICATIONS_URL, () =>
        HttpResponse.json({ items: [upcoming(), digest()], nextCursor: null })
      )
    )

    renderPage()

    expect(
      await screen.findByText('Через 30 минут запись')
    ).toBeInTheDocument()
    expect(
      screen.getByText('14:00 · стрижка · «Барбершоп на Ленина»')
    ).toBeInTheDocument()
    expect(screen.getByText('Сегодня 4 записи')).toBeInTheDocument()
    expect(screen.getByText('Первая в 10:00')).toBeInTheDocument()
    expect(screen.getAllByRole('img', { name: 'Не прочитано' })).toHaveLength(2)
    expect(screen.getAllByText('Открыть')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Прочитать все' })).toBeEnabled()
  })

  it('«Прочитать все» шлёт read-all, перечитывает список и гасит кнопку', async () => {
    const user = userEvent.setup()
    const readAll: string[] = []
    let listCalls = 0

    server.use(
      http.get(NOTIFICATIONS_URL, () => {
        listCalls += 1

        return HttpResponse.json({
          items:
            listCalls === 1
              ? [upcoming(), digest()]
              : [
                  upcoming('2026-11-07T10:00:00Z'),
                  digest('2026-11-07T10:00:00Z'),
                ],
          nextCursor: null,
        })
      }),
      http.post('/api/barber/notifications/read-all', ({ request }) => {
        readAll.push(new URL(request.url).pathname)

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()

    const button = await screen.findByRole('button', {
      name: 'Прочитать все',
    })

    await waitFor(() => {
      expect(button).toBeEnabled()
    })
    await user.click(button)

    expect(
      await screen.findByText('Все уведомления прочитаны.')
    ).toBeInTheDocument()

    expect(readAll).toStrictEqual(['/api/barber/notifications/read-all'])
    expect(listCalls).toBe(2)
    expect(button).toBeDisabled()
    expect(
      screen.queryByRole('img', { name: 'Не прочитано' })
    ).not.toBeInTheDocument()
  })

  it('клик по строке отмечает уведомление и открывает кабинет', async () => {
    const user = userEvent.setup()
    const reads: string[] = []

    server.use(
      http.get(NOTIFICATIONS_URL, () =>
        HttpResponse.json({ items: [upcoming()], nextCursor: null })
      ),
      http.post('/api/barber/notifications/:id/read', ({ request }) => {
        reads.push(new URL(request.url).pathname)

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()

    await user.click(
      await screen.findByRole('button', { name: /Через 30 минут запись/ })
    )

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })

    expect(reads).toStrictEqual([
      `/api/barber/notifications/${UPCOMING_ID}/read`,
    ])
  })
})

describe('BarberNotificationsPage, настройки', () => {
  it('рисует checkbox-ы, time со шагом 900 и селект 15/30/60', async () => {
    renderPage()

    expect(
      await screen.findByText('Присылать напоминания')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('checkbox', { name: 'Присылать напоминания' })
    ).toBeChecked()
    expect(
      screen.getByRole('checkbox', { name: 'Утренний дайджест' })
    ).toBeChecked()

    const time = screen.getByLabelText('Время утреннего дайджеста')

    expect(time).toHaveAttribute('type', 'time')
    expect(time).toHaveAttribute('step', '900')

    const lead = screen.getByLabelText('Напомнить за')

    expect(lead).toHaveValue('30')
    expect(
      screen.getByRole('option', { name: '15 минут' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('option', { name: '60 минут' })
    ).toBeInTheDocument()
  })

  it('сохраняет настройку одной PUT после дебаунса', async () => {
    const user = userEvent.setup()
    let body: unknown = null

    server.use(
      http.put('/api/barber/notification-settings', async ({ request }) => {
        body = await request.json()

        return HttpResponse.json({ ...notificationSettings, leadMinutes: 60 })
      })
    )

    renderPage()

    await screen.findByText('Напомнить за')
    await user.selectOptions(screen.getByLabelText('Напомнить за'), '60')

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(body).toMatchObject({
      isEnabled: true,
      digestEnabled: true,
      digestTime: '08:00',
      leadMinutes: 60,
    })
  })

  it('показывает ошибку 400 у поля «Напомнить за»', async () => {
    const user = userEvent.setup()

    server.use(
      http.put('/api/barber/notification-settings', () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['notificationSettings.leadMinutes'],
          }),
          { status: 400 }
        )
      )
    )

    renderPage()

    await screen.findByText('Напомнить за')
    await user.selectOptions(screen.getByLabelText('Напомнить за'), '60')

    expect(
      await screen.findByText('Напомнить можно за 15, 30 или 60 минут.')
    ).toBeInTheDocument()
  })

  it('при botConfigured: false показывает объяснение и выключенную кнопку', async () => {
    server.use(
      http.get('/api/barber/notification-settings', () =>
        HttpResponse.json({ ...notificationSettings, botConfigured: false })
      )
    )

    renderPage()

    expect(
      await screen.findByText(
        'Telegram не настроен. Уведомления будут видны только здесь.'
      )
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Подключить' })
    ).toBeDisabled()
  })

  it('при telegramLinked: true показывает «Подключён» и «Отключить»', async () => {
    server.use(
      http.get('/api/barber/notification-settings', () =>
        HttpResponse.json({
          ...notificationSettings,
          telegramLinked: true,
          telegramLinkedAt: '2026-11-07T09:00:00Z',
        })
      )
    )

    renderPage()

    expect(await screen.findByText('Подключён')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Отключить' })
    ).toBeInTheDocument()
  })

  it('создаёт код привязки и показывает ссылку «Открыть Telegram»', async () => {
    const user = userEvent.setup()
    const deepLink = 'https://t.me/barbersearch_bot?start=ABCDEF'

    server.use(
      http.post('/api/barber/notifications/telegram/link', () =>
        HttpResponse.json({
          code: 'ABCDEF',
          deepLink,
          expiresAt: '2026-11-07T09:10:00Z',
        })
      )
    )

    renderPage()

    await user.click(
      await screen.findByRole('button', { name: 'Подключить' })
    )

    expect(await screen.findByText('ABCDEF')).toBeInTheDocument()

    const link = screen.getByRole('link', { name: 'Открыть Telegram' })

    expect(link).toHaveAttribute('href', deepLink)
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noreferrer noopener')
    expect(
      screen.getByRole('button', { name: 'Проверить' })
    ).toBeInTheDocument()
  })

  it('показывает ошибку загрузки настроек отдельно от списка', async () => {
    server.use(
      http.get('/api/barber/notification-settings', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderPage()

    expect(
      await screen.findByText('Не удалось загрузить настройки.')
    ).toBeInTheDocument()
  })
})
