import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  createSalon,
  describeAdminCatalogueError,
  fetchAdminHairServices,
  fetchAdminSalons,
  setHairServiceActive,
} from '@/lib/admin-catalogue'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Транспорт админского каталога.
 *
 * Списки отдаются целиком (без курсора и размера страницы), поэтому контракт здесь —
 * ровно два параметра: `status` всегда, `search` только непустым. Мутации идут через
 * cookie-сессию с antiforgery-токеном, как остальные админские действия.
 */

const HAIR_SERVICES_URL = '/api/admin/catalogue/hair-services'

/** Перехватывает GET списка и складывает параметры каждого запроса. */
function captureList(path: string): URLSearchParams[] {
  const captured: URLSearchParams[] = []

  server.use(
    http.get(path, ({ request }) => {
      captured.push(new URL(request.url).searchParams)

      return HttpResponse.json([])
    })
  )

  return captured
}

function entries(params: URLSearchParams): [string, string][] {
  return [...params.entries()]
}

describe('fetchAdminHairServices', () => {
  it('всегда отправляет status, по умолчанию all', async () => {
    const captured = captureList(HAIR_SERVICES_URL)

    await expect(fetchAdminHairServices({ status: 'all' })).resolves.toStrictEqual([])

    expect(captured).toHaveLength(1)
    expect(entries(captured[0])).toStrictEqual([['status', 'all']])
  })

  it('передаёт статус и поиск в одном запросе', async () => {
    const captured = captureList('/api/admin/catalogue/salons')

    await fetchAdminSalons({ status: 'hidden', search: 'Норд' })

    expect(entries(captured[0])).toStrictEqual([
      ['status', 'hidden'],
      ['search', 'Норд'],
    ])
  })

  it('не отправляет пустой поиск', async () => {
    const captured = captureList(HAIR_SERVICES_URL)

    await fetchAdminHairServices({ status: 'active', search: '' })

    expect(entries(captured[0])).toStrictEqual([['status', 'active']])
  })

  it('битый ответ списка — ошибка', async () => {
    server.use(
      http.get(HAIR_SERVICES_URL, () => HttpResponse.json({ items: [] }))
    )

    await expect(fetchAdminHairServices({ status: 'all' })).rejects.toThrow(ApiError)
  })

  it('лишние поля ответа не ломают guard', async () => {
    server.use(
      http.get(HAIR_SERVICES_URL, () =>
        HttpResponse.json([
          {
            id: '11111111-1111-4111-8111-111111111111',
            name: 'Фейд',
            category: 'Haircut',
            defaultDurationMinutes: 45,
            description: null,
            isActive: true,
            usageCount: 3,
            internalNote: 'лишнее',
          },
        ])
      )
    )

    const rows = await fetchAdminHairServices({ status: 'all' })

    expect(rows).toHaveLength(1)
    expect(rows[0].name).toBe('Фейд')
    expect(rows[0].usageCount).toBe(3)
  })
})

describe('setHairServiceActive', () => {
  const serviceId = '22222222-2222-4222-8222-222222222222'

  type CapturedMutation = { method: string; csrfToken: string | null }

  function captureActivation(): CapturedMutation[] {
    const calls: CapturedMutation[] = []

    server.use(
      http.delete(`/api/hair-services/${serviceId}/activation`, ({ request }) => {
        calls.push({
          method: request.method,
          csrfToken: request.headers.get('X-CSRF-TOKEN'),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    return calls
  }

  it('выключает услугу DELETE-запросом с CSRF-заголовком', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    const calls = captureActivation()

    await expect(setHairServiceActive(serviceId, false)).resolves.toBeUndefined()

    expect(calls).toStrictEqual([{ method: 'DELETE', csrfToken: 'csrf-token' }])
    expect(csrfCalls()).toBe(1)
  })

  it('включает услугу POST-запросом на тот же адрес', async () => {
    const calls: CapturedMutation[] = []

    server.use(
      http.post(`/api/hair-services/${serviceId}/activation`, ({ request }) => {
        calls.push({ method: request.method, csrfToken: request.headers.get('X-CSRF-TOKEN') })

        return new HttpResponse(null, { status: 204 })
      })
    )

    await setHairServiceActive(serviceId, true)

    expect(calls).toStrictEqual([{ method: 'POST', csrfToken: 'csrf-token' }])
  })

  it('повторяет один раз на auth.csrf.invalid', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let csrfIndex = 0
    let attempts = 0

    server.use(
      http.get('/api/auth/csrf', () => {
        csrfIndex += 1

        return HttpResponse.json({ token: `csrf-${csrfIndex}` })
      }),
      http.delete(`/api/hair-services/${serviceId}/activation`, ({ request }) => {
        attempts += 1

        if (request.headers.get('X-CSRF-TOKEN') === 'csrf-1') {
          return HttpResponse.json(
            problemDetails(400, { errorCodes: ['auth.csrf.invalid'] }),
            { status: 400 }
          )
        }

        return new HttpResponse(null, { status: 204 })
      })
    )

    await setHairServiceActive(serviceId, false)

    expect(attempts).toBe(2)
    expect(csrfCalls()).toBe(2)
  })
})

describe('createSalon', () => {
  const input = {
    name: 'E2E Studio Nord',
    address: 'ул. Северная, 2',
    city: 'Санкт-Петербург',
    country: 'Russia',
    timeZoneId: 'Europe/Moscow',
    latitude: 59.9386,
    longitude: 30.3141,
    postalCode: '190000',
    phoneNumber: '+78120000000',
    websiteUrl: null,
  }

  it('отправляет все поля формы одним POST', async () => {
    const bodies: unknown[] = []

    server.use(
      http.post('/api/salons', async ({ request }) => {
        bodies.push(await request.json())

        return HttpResponse.json({ id: 'salon-1' }, { status: 201 })
      })
    )

    await expect(createSalon(input)).resolves.toStrictEqual({ id: 'salon-1' })
    expect(bodies).toStrictEqual([input])
  })
})

/**
 * Ошибки экрана: причину показываем текстом, английский `detail` и код статуса
 * наружу не выходят.
 */
describe('describeAdminCatalogueError', () => {
  it('объясняет конфликт правки', () => {
    const error = new ApiError(
      409,
      problemDetails(409, { errorCodes: ['specialization.update.concurrencyConflict'] }),
      'Conflict.'
    )

    expect(describeAdminCatalogueError(error, 'Не удалось.')).toBe(
      'Запись изменили одновременно. Обновите список.'
    )
  })

  it('объясняет неизвестный часовой пояс у поля', () => {
    const error = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['salon.timeZoneId.unknown'] }),
      'Invalid.'
    )

    expect(describeAdminCatalogueError(error, 'Не удалось.')).toBe(
      'Неизвестный часовой пояс. Выберите зону из списка.'
    )
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(describeAdminCatalogueError(new Error('сеть'), 'Не удалось.')).toBe(
      'Не удалось.'
    )
  })
})
