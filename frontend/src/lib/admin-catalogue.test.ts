import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  approveSalon,
  createHairService,
  createSalon,
  createSpecialization,
  describeAdminCatalogueError,
  describeSalonModerationError,
  fetchAdminHairServices,
  fetchAdminSalons,
  rejectSalon,
  renameSpecialization,
  setHairServiceActive,
  updateHairService,
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

  it('очередь проверки салонов уходит как status=pending', async () => {
    const captured = captureList('/api/admin/catalogue/salons')

    await fetchAdminSalons({ status: 'pending' })

    expect(entries(captured[0])).toStrictEqual([['status', 'pending']])
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
            specializationId: '33333333-3333-4333-8333-333333333333',
            specializationName: 'Барберинг',
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

describe('мутации услуг и специализаций', () => {
  const SPECIALIZATION_ID = '44444444-4444-4444-8444-444444444444'

  /** Перехватывает мутацию и складывает тело запроса. */
  function captureBody(path: string, status = 204): unknown[] {
    const bodies: unknown[] = []

    server.use(
      http.post(path, async ({ request }) => {
        bodies.push(await request.json())

        return status === 201
          ? HttpResponse.json({ id: 'created-id' }, { status: 201 })
          : new HttpResponse(null, { status })
      }),
      http.put(path, async ({ request }) => {
        bodies.push(await request.json())

        return new HttpResponse(null, { status: 204 })
      })
    )

    return bodies
  }

  it('создание услуги отправляет specializationId и не отправляет category', async () => {
    const bodies = captureBody('/api/hair-services', 201)

    await expect(
      createHairService({
        name: 'Фейд',
        specializationId: SPECIALIZATION_ID,
        defaultDurationMinutes: 45,
        description: null,
      })
    ).resolves.toStrictEqual({ id: 'created-id' })

    expect(bodies).toStrictEqual([
      {
        name: 'Фейд',
        specializationId: SPECIALIZATION_ID,
        defaultDurationMinutes: 45,
        description: null,
      },
    ])
    expect(bodies[0]).not.toHaveProperty('category')
  })

  it('правка услуги передаёт новую специализацию', async () => {
    const url = '/api/hair-services/55555555-5555-4555-8555-555555555555'
    const bodies = captureBody(url)

    await updateHairService('55555555-5555-4555-8555-555555555555', {
      name: 'Фейд',
      description: 'Короткая стрижка',
      defaultDurationMinutes: 60,
      specializationId: SPECIALIZATION_ID,
    })

    expect(bodies).toStrictEqual([
      {
        name: 'Фейд',
        description: 'Короткая стрижка',
        defaultDurationMinutes: 60,
        specializationId: SPECIALIZATION_ID,
      },
    ])
    expect(bodies[0]).not.toHaveProperty('category')
  })

  it('создание специализации отправляет обязательную категорию', async () => {
    const bodies = captureBody('/api/specializations', 201)

    await expect(
      createSpecialization({
        name: 'Массаж',
        category: 'Other',
        description: null,
      })
    ).resolves.toStrictEqual({ id: 'created-id' })

    expect(bodies).toStrictEqual([
      { name: 'Массаж', category: 'Other', description: null },
    ])
  })

  it('правка специализации меняет категорию вместе с названием', async () => {
    const url =
      '/api/specializations/66666666-6666-4666-8666-666666666666'
    const bodies = captureBody(url)

    await renameSpecialization('66666666-6666-4666-8666-666666666666', {
      name: 'Оформление бороды',
      category: 'Beard',
      description: null,
    })

    expect(bodies).toStrictEqual([
      { name: 'Оформление бороды', category: 'Beard', description: null },
    ])
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
    kind: 'Salon' as const,
    isListed: true,
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

describe('модерация салона', () => {
  const salonId = '33333333-3333-4333-8333-333333333333'

  type CapturedAction = {
    method: string
    path: string
    csrfToken: string | null
    body: string
  }

  function capture(path: string): CapturedAction[] {
    const calls: CapturedAction[] = []

    server.use(
      http.post(path, async ({ request }) => {
        calls.push({
          method: request.method,
          path: new URL(request.url).pathname,
          csrfToken: request.headers.get('X-CSRF-TOKEN'),
          body: await request.text(),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    return calls
  }

  it('одобрение — POST без тела и с CSRF', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    const calls = capture(`/api/salons/${salonId}/approval`)

    await expect(approveSalon(salonId)).resolves.toBeUndefined()

    expect(csrfCalls()).toBe(1)
    expect(calls).toStrictEqual([
      {
        method: 'POST',
        path: `/api/salons/${salonId}/approval`,
        csrfToken: 'csrf-token',
        body: '',
      },
    ])
  })

  it('отказ отправляет причину в теле', async () => {
    const calls = capture(`/api/salons/${salonId}/rejection`)

    await expect(
      rejectSalon(salonId, 'Не подтверждён адрес')
    ).resolves.toBeUndefined()

    expect(calls).toStrictEqual([
      {
        method: 'POST',
        path: `/api/salons/${salonId}/rejection`,
        csrfToken: 'csrf-token',
        body: JSON.stringify({ reason: 'Не подтверждён адрес' }),
      },
    ])
  })

  it('переводит 400 причины, 409 повторного решения и 403', () => {
    const reasonRequired = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['salon.reject.reasonRequired'] }),
      'Invalid.'
    )
    const reasonTooLong = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['salon.reject.reasonTooLong'] }),
      'Invalid.'
    )
    const invalidStatus = new ApiError(
      409,
      problemDetails(409, { errorCodes: ['salon.review.invalidStatus'] }),
      'Conflict.'
    )
    const forbidden = new ApiError(403, problemDetails(403), 'Forbidden.')

    expect(describeSalonModerationError(reasonRequired, 'reject')).toBe(
      'Укажите причину отказа.'
    )
    expect(describeSalonModerationError(reasonTooLong, 'reject')).toBe(
      'Слишком длинная причина — максимум 500 символов.'
    )
    expect(describeSalonModerationError(invalidStatus, 'approve')).toBe(
      'Салон уже проверили. Обновите список.'
    )
    expect(describeSalonModerationError(forbidden, 'approve')).toBe(
      'Раздел доступен только администратору.'
    )
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

  it('объясняет пустую или исчезнувшую специализацию услуги', () => {
    const empty = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['hairService.specializationId'] }),
      'Invalid.'
    )
    const missing = new ApiError(
      404,
      problemDetails(404, { errorCodes: ['hairService.specialization.notFound'] }),
      'Not found.'
    )

    expect(describeAdminCatalogueError(empty, 'Не удалось.')).toBe(
      'Выберите специализацию для услуги.'
    )
    // Код важнее общего 404: текст говорит, что именно исчезло.
    expect(describeAdminCatalogueError(missing, 'Не удалось.')).toBe(
      'Специализация не найдена — обновите список.'
    )
  })

  it('объясняет неизвестную категорию специализации', () => {
    const error = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['specialization.category'] }),
      'Invalid.'
    )

    expect(describeAdminCatalogueError(error, 'Не удалось.')).toBe(
      'Неизвестная категория. Выберите категорию из списка.'
    )
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(describeAdminCatalogueError(new Error('сеть'), 'Не удалось.')).toBe(
      'Не удалось.'
    )
  })
})
