import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  createTelegramLink,
  describeNotificationSettingsFailure,
  describeNotificationsError,
  describeTelegramLinkError,
  fetchNotifications,
  fetchSettings,
  fetchUnreadCount,
  markAllRead,
  markNotificationRead,
  unlinkTelegram,
  updateSettings,
} from '@/lib/barber-notifications'
import { notificationSettings, problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const NOTIFICATION_ID = '11111111-1111-4111-8111-111111111111'
const BOOKING_ID = '22222222-2222-4222-8222-222222222222'

const UPCOMING = {
  id: NOTIFICATION_ID,
  kind: 'Upcoming',
  createdAt: '2026-11-07T09:02:00Z',
  readAt: null,
  bookingId: BOOKING_ID,
  startAt: '2026-11-07T09:00:00Z',
  serviceName: 'стрижка',
  salonName: 'Барбершоп на Ленина',
  salonTimeZoneId: 'Asia/Yekaterinburg',
  bookingsCount: null,
  delivery: 'Sent',
}

describe('fetchNotifications', () => {
  it('просит первую страницу с pageSize=20 и без cursor', async () => {
    let url = ''

    server.use(
      http.get('/api/barber/notifications', ({ request }) => {
        url = request.url

        return HttpResponse.json({ items: [UPCOMING], nextCursor: null })
      })
    )

    const page = await fetchNotifications()

    expect(new URL(url).searchParams.get('pageSize')).toBe('20')
    expect(new URL(url).searchParams.has('cursor')).toBe(false)
    expect(page.items).toHaveLength(1)
    expect(page.nextCursor).toBeNull()
  })

  it('передаёт непрозрачный cursor следующей страницы', async () => {
    let url = ''

    server.use(
      http.get('/api/barber/notifications', ({ request }) => {
        url = request.url

        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    await fetchNotifications('cursor-2')

    expect(new URL(url).searchParams.get('cursor')).toBe('cursor-2')
  })

  it('превращает неизвестный kind в обычную русскую ошибку', async () => {
    server.use(
      http.get('/api/barber/notifications', () =>
        HttpResponse.json({
          items: [{ ...UPCOMING, kind: 'Reminder' }],
          nextCursor: null,
        })
      )
    )

    await expect(fetchNotifications()).rejects.toMatchObject({
      status: 200,
      message: 'Сервер вернул неожиданный ответ.',
    })
  })
})

describe('мутации уведомлений', () => {
  it('отмечает уведомление прочитанным POST-ом с CSRF-заголовком', async () => {
    const requests: { url: string; csrf: string | null }[] = []

    server.use(
      http.post(
        '/api/barber/notifications/:id/read',
        ({ request }) => {
          requests.push({
            url: request.url,
            csrf: request.headers.get('x-csrf-token'),
          })

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    await markNotificationRead(NOTIFICATION_ID)

    expect(requests).toStrictEqual([
      {
        url: `http://localhost:5173/api/barber/notifications/${NOTIFICATION_ID}/read`,
        csrf: 'csrf-token',
      },
    ])
  })

  it('читает все уведомления одним POST read-all', async () => {
    let url = ''
    let csrf: string | null = null

    server.use(
      http.post('/api/barber/notifications/read-all', ({ request }) => {
        url = request.url
        csrf = request.headers.get('x-csrf-token')

        return new HttpResponse(null, { status: 204 })
      })
    )

    await markAllRead()

    expect(new URL(url).pathname).toBe('/api/barber/notifications/read-all')
    expect(csrf).toBe('csrf-token')
  })
})

describe('счётчик и настройки', () => {
  it('читает count непрочитанных и отвергает отрицательное значение', async () => {
    server.use(
      http.get('/api/barber/notifications/unread-count', () =>
        HttpResponse.json({ count: 3 })
      )
    )

    await expect(fetchUnreadCount()).resolves.toStrictEqual({ count: 3 })

    server.use(
      http.get('/api/barber/notifications/unread-count', () =>
        HttpResponse.json({ count: -1 })
      )
    )

    await expect(fetchUnreadCount()).rejects.toBeInstanceOf(ApiError)
  })

  it('читает настройки и отвергает TimeOnly-сериализацию digestTime', async () => {
    await expect(fetchSettings()).resolves.toStrictEqual(notificationSettings)

    server.use(
      http.get('/api/barber/notification-settings', () =>
        HttpResponse.json({ ...notificationSettings, digestTime: '08:00:00' })
      )
    )

    await expect(fetchSettings()).rejects.toMatchObject({
      status: 200,
      message: 'Сервер вернул неожиданный ответ.',
    })
  })

  it('сохраняет настройки одной PUT с полным набором полей', async () => {
    let method = ''
    let body: unknown = null
    let csrf: string | null = null

    server.use(
      http.put('/api/barber/notification-settings', async ({ request }) => {
        method = request.method
        body = await request.json()
        csrf = request.headers.get('x-csrf-token')

        return HttpResponse.json({ ...notificationSettings, leadMinutes: 60 })
      })
    )

    const saved = await updateSettings({
      isEnabled: true,
      digestEnabled: true,
      digestTime: '08:00',
      leadMinutes: 60,
    })

    expect(method).toBe('PUT')
    expect(csrf).toBe('csrf-token')
    expect(body).toStrictEqual({
      isEnabled: true,
      digestEnabled: true,
      digestTime: '08:00',
      leadMinutes: 60,
    })
    expect(saved.leadMinutes).toBe(60)
  })
})

describe('Telegram', () => {
  it('создаёт ссылку привязки и отвязывает чат', async () => {
    const paths: string[] = []

    server.use(
      http.post('/api/barber/notifications/telegram/link', ({ request }) => {
        paths.push(new URL(request.url).pathname)

        return HttpResponse.json({
          code: 'ABCDEF',
          deepLink: 'https://t.me/barbersearch_bot?start=ABCDEF',
          expiresAt: '2026-11-07T09:10:00Z',
        })
      }),
      http.post('/api/barber/notifications/telegram/unlink', ({ request }) => {
        paths.push(new URL(request.url).pathname)

        return new HttpResponse(null, { status: 204 })
      })
    )

    await expect(createTelegramLink()).resolves.toStrictEqual({
      code: 'ABCDEF',
      deepLink: 'https://t.me/barbersearch_bot?start=ABCDEF',
      expiresAt: '2026-11-07T09:10:00Z',
    })
    await unlinkTelegram()

    expect(paths).toStrictEqual([
      '/api/barber/notifications/telegram/link',
      '/api/barber/notifications/telegram/unlink',
    ])
  })
})

describe('ошибки уведомлений', () => {
  it('не показывает английский detail и сохраняет русский фолбэк', () => {
    const error = new ApiError(
      500,
      problemDetails(500, { detail: 'Internal server error.' }),
      'Internal server error.'
    )

    expect(describeNotificationsError(error)).toBe(
      'Не удалось загрузить уведомления.'
    )
    expect(describeNotificationsError(new ApiError(0, null, 'Network'))).toBe(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
    expect(describeNotificationsError(new ApiError(401, null, '401'))).toBe(
      'Сессия истекла — войдите снова.'
    )
  })

  it('возвращает 400 настроек полем, а не общей строкой', () => {
    const lead = new ApiError(
      400,
      problemDetails(400, {
        errorCodes: ['notificationSettings.leadMinutes'],
      }),
      'Validation.'
    )
    const digest = new ApiError(
      400,
      problemDetails(400, {
        errorCodes: ['notificationSettings.digestTime'],
      }),
      'Validation.'
    )

    expect(describeNotificationSettingsFailure(lead)).toStrictEqual({
      field: 'leadMinutes',
      message: 'Напомнить можно за 15, 30 или 60 минут.',
    })
    expect(describeNotificationSettingsFailure(digest)).toStrictEqual({
      field: 'digestTime',
      message: 'Время дайджеста — с 06:00 до 12:00, кратно 15 минутам.',
    })
  })

  it('оставляет сеть и 429 общей ошибкой сохранения', () => {
    expect(
      describeNotificationSettingsFailure(new ApiError(429, null, '429'))
    ).toStrictEqual({
      field: null,
      message: 'Слишком много запросов. Подождите минуту и повторите.',
    })
    expect(
      describeNotificationSettingsFailure(
        new ApiError(500, problemDetails(500), 'Internal.')
      )
    ).toStrictEqual({
      field: null,
      message: 'Не удалось сохранить настройки.',
    })
  })

  it('объясняет уже привязанный Telegram по коду 409', () => {
    const error = new ApiError(
      409,
      problemDetails(409, {
        errorCodes: ['notification.telegram.alreadyLinked'],
      }),
      'Conflict.'
    )

    expect(describeTelegramLinkError(error)).toBe(
      'Telegram уже подключён. Обновите страницу.'
    )
  })
})
