import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import {
  useNotificationSettings,
  NOTIFICATION_SETTINGS_DEBOUNCE_MS,
} from '@/hooks/use-notification-settings'
import { notificationSettings, problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

describe('useNotificationSettings', () => {
  it('читает настройки один раз и отдаёт значения по умолчанию', async () => {
    const { result } = renderHook(() => useNotificationSettings())

    expect(result.current.status).toBe('loading')

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    expect(result.current.settings).toStrictEqual(notificationSettings)
    expect(result.current.fieldErrors).toStrictEqual({})
    expect(result.current.saveError).toBeNull()
  })

  it('сохраняет одну PUT на пачку правок после дебаунса 500 мс', async () => {
    const bodies: unknown[] = []

    server.use(
      http.put('/api/barber/notification-settings', async ({ request }) => {
        bodies.push(await request.json())

        return HttpResponse.json({
          ...notificationSettings,
          leadMinutes: 60,
          digestTime: '09:00',
        })
      })
    )

    const { result } = renderHook(() => useNotificationSettings())

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    act(() => {
      result.current.update({ leadMinutes: 60 })
      result.current.update({ digestTime: '09:00' })
    })

    await waitFor(
      () => {
        expect(bodies).toHaveLength(1)
      },
      { timeout: NOTIFICATION_SETTINGS_DEBOUNCE_MS + 2000 }
    )

    expect(bodies[0]).toStrictEqual({
      isEnabled: true,
      digestEnabled: true,
      digestTime: '09:00',
      leadMinutes: 60,
    })
    expect(result.current.settings?.leadMinutes).toBe(60)
    expect(result.current.settings?.digestTime).toBe('09:00')
    expect(result.current.fieldErrors).toStrictEqual({})
  })

  it('применяет нормализованный ответ сервера, пока новых правок нет', async () => {
    server.use(
      http.put('/api/barber/notification-settings', () =>
        HttpResponse.json({
          ...notificationSettings,
          leadMinutes: 60,
          digestTime: '09:15',
        })
      )
    )

    const { result } = renderHook(() => useNotificationSettings())

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    act(() => {
      result.current.update({ leadMinutes: 60, digestTime: '09:00' })
    })

    await waitFor(
      () => {
        expect(result.current.settings?.digestTime).toBe('09:15')
      },
      { timeout: NOTIFICATION_SETTINGS_DEBOUNCE_MS + 2000 }
    )

    expect(result.current.settings?.leadMinutes).toBe(60)
  })

  it('показывает 400 notificationSettings.leadMinutes у поля', async () => {
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

    const { result } = renderHook(() => useNotificationSettings())

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    act(() => {
      result.current.update({ leadMinutes: 60 })
    })

    await waitFor(
      () => {
        expect(result.current.fieldErrors.leadMinutes).toBe(
          'Напомнить можно за 15, 30 или 60 минут.'
        )
      },
      { timeout: NOTIFICATION_SETTINGS_DEBOUNCE_MS + 2000 }
    )

    expect(result.current.fieldErrors.digestTime).toBeUndefined()
    expect(result.current.saveError).toBeNull()
    expect(result.current.isSaving).toBe(false)
  })

  it('показывает 400 notificationSettings.digestTime у поля', async () => {
    server.use(
      http.put('/api/barber/notification-settings', () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['notificationSettings.digestTime'],
          }),
          { status: 400 }
        )
      )
    )

    const { result } = renderHook(() => useNotificationSettings())

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    act(() => {
      result.current.update({ digestTime: '08:15' })
    })

    await waitFor(
      () => {
        expect(result.current.fieldErrors.digestTime).toBe(
          'Время дайджеста — с 06:00 до 12:00, кратно 15 минутам.'
        )
      },
      { timeout: NOTIFICATION_SETTINGS_DEBOUNCE_MS + 2000 }
    )
  })

  it('оставляет сетевую ошибку общей строкой сохранения', async () => {
    server.use(
      http.put('/api/barber/notification-settings', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    const { result } = renderHook(() => useNotificationSettings())

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    act(() => {
      result.current.update({ isEnabled: false })
    })

    await waitFor(
      () => {
        expect(result.current.saveError).toBe('Не удалось сохранить настройки.')
      },
      { timeout: NOTIFICATION_SETTINGS_DEBOUNCE_MS + 2000 }
    )

    expect(result.current.fieldErrors).toStrictEqual({})
  })

  it('не прячет форму при ошибке чтения, но отдаёт текст ошибки', async () => {
    server.use(
      http.get('/api/barber/notification-settings', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    const { result } = renderHook(() => useNotificationSettings())

    await waitFor(() => {
      expect(result.current.status).toBe('error')
    })

    expect(result.current.settings).toBeNull()
    expect(result.current.loadError).toBe('Не удалось загрузить настройки.')
  })
})
