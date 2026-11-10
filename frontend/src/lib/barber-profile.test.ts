import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  addBarberService,
  addBarberSpecialization,
  describeBarberProfileError,
  fetchOwnBarberProfile,
  removeBarberService,
  removeBarberSpecialization,
  updateBarberProfile,
  updateBarberService,
  type BarberProfileUpdateDraft,
} from '@/lib/barber-profile'
import { testBarberProfile } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Транспорт своего профиля барбера.
 *
 * `GET /api/barber-profiles/{id}` — единственное чтение, которое отдаёт скрытые
 * услуги, поэтому контракт проверяется строгим guard'ом: битое тело должно стать
 * `ApiError`, а не молчаливой карточкой без услуг. Все мутации — cookie-запросы
 * с `X-CSRF-TOKEN`, их тело — контракт сервера, а не удобство UI.
 */

const PROFILE_ID = testBarberProfile.id
const HAIR_SERVICE_ID = testBarberProfile.services[0].hairServiceId
const SPECIALIZATION_ID = testBarberProfile.specializations[0].specializationId
const PROFILE_URL = `/api/barber-profiles/${PROFILE_ID}`

const DRAFT: BarberProfileUpdateDraft = {
  displayName: 'Иван Петров',
  yearsOfExperience: 10,
  bio: 'Стригу с 2015 года.',
  languages: ['Ru', 'En'],
  certificatesAndTraining: ['Курс барберинга'],
  achievements: [],
  paymentMethods: ['Cash', 'Card'],
  communicationFormat: ['В салоне'],
  goodies: [],
  additionalServices: ['Укладка'],
  // Правка отправляет строку: контракт объявляет `string LatePolicy`, не `null`.
  latePolicy: '',
}

describe('fetchOwnBarberProfile', () => {
  it('читает карточку по id и принимает скрытые услуги', async () => {
    const requested: string[] = []

    server.use(
      http.get(PROFILE_URL, ({ request }) => {
        requested.push(request.url)

        return HttpResponse.json({ ...testBarberProfile, extra: 'поле' })
      })
    )

    const profile = await fetchOwnBarberProfile(PROFILE_ID)

    expect(profile.services).toHaveLength(2)
    expect(profile.services.some((service) => !service.isActive)).toBe(true)
    expect(new URL(requested[0]).pathname).toBe(PROFILE_URL)
    expect(new URL(requested[0]).search).toBe('')
  })

  it('битый ответ чтения — ApiError, а не молчаливый рендер', async () => {
    const [first] = testBarberProfile.services

    server.use(
      http.get(PROFILE_URL, () =>
        HttpResponse.json({
          ...testBarberProfile,
          services: [{ ...first, defaultDurationMinutes: '45' }],
        })
      )
    )

    await expect(fetchOwnBarberProfile(PROFILE_ID)).rejects.toBeInstanceOf(
      ApiError
    )
    await expect(fetchOwnBarberProfile(PROFILE_ID)).rejects.toThrow(
      'Сервер вернул неожиданный ответ.'
    )
  })

  it('поддерживает signal: отменённый запрос не доходит до сервера', async () => {
    const controller = new AbortController()
    controller.abort()

    await expect(
      fetchOwnBarberProfile(PROFILE_ID, controller.signal)
    ).rejects.toMatchObject({ status: 0 })
  })
})

type MutationCase = {
  name: string
  method: string
  path: string
  body: unknown
  run: () => Promise<void>
}

const MUTATIONS: readonly MutationCase[] = [
  {
    name: 'правка карточки',
    method: 'PUT',
    path: PROFILE_URL,
    body: DRAFT,
    run: () => updateBarberProfile(PROFILE_ID, DRAFT),
  },
  {
    name: 'добавление услуги',
    method: 'POST',
    path: `${PROFILE_URL}/services`,
    body: {
      hairServiceId: HAIR_SERVICE_ID,
      price: 2000,
      durationMinutes: 60,
    },
    run: () =>
      addBarberService(PROFILE_ID, {
        hairServiceId: HAIR_SERVICE_ID,
        price: 2000,
        durationMinutes: 60,
      }),
  },
  {
    name: 'правка цены и длительности',
    method: 'PUT',
    path: `${PROFILE_URL}/services/${HAIR_SERVICE_ID}`,
    body: { price: 2000, durationMinutes: 60 },
    run: () =>
      updateBarberService(PROFILE_ID, HAIR_SERVICE_ID, {
        price: 2000,
        durationMinutes: 60,
      }),
  },
  {
    name: 'скрытие услуги',
    method: 'DELETE',
    path: `${PROFILE_URL}/services/${HAIR_SERVICE_ID}`,
    body: null,
    run: () => removeBarberService(PROFILE_ID, HAIR_SERVICE_ID),
  },
  {
    name: 'добавление специализации',
    method: 'POST',
    path: `${PROFILE_URL}/specializations/${SPECIALIZATION_ID}`,
    body: null,
    run: () => addBarberSpecialization(PROFILE_ID, SPECIALIZATION_ID),
  },
  {
    name: 'удаление специализации',
    method: 'DELETE',
    path: `${PROFILE_URL}/specializations/${SPECIALIZATION_ID}`,
    body: null,
    run: () => removeBarberSpecialization(PROFILE_ID, SPECIALIZATION_ID),
  },
]

describe('мутации профиля', () => {
  // Отдельные `it`, а не `it.each`: кейсы делят один MSW-сервер, и параллельный
  // запуск `each` в Vitest успевал сбросить обработчик до запроса.
  for (const mutation of MUTATIONS) {
    it(`${mutation.name}: ${mutation.method} ${mutation.path}, CSRF и тело`, async () => {
      const { method, path, body, run } = mutation
      let actualMethod = ''
      let actualToken: string | null = null
      let actualBody: unknown
      const csrfRequests = countRequests('/api/auth/csrf')

      server.use(
        http.all(path, async ({ request }) => {
          actualMethod = request.method
          actualToken = request.headers.get('X-CSRF-TOKEN')
          const text = await request.text()
          actualBody = text === '' ? null : JSON.parse(text)

          return new HttpResponse(null, { status: 204 })
        })
      )

      await run()

      expect(actualMethod).toBe(method)
      expect(actualToken).toBe('csrf-token')
      expect(actualBody).toEqual(body)
      expect(csrfRequests()).toBe(1)
    })
  }

  it('правка не отправляет grade, latePolicy остаётся строкой', async () => {
    const bodies: unknown[] = []

    server.use(
      http.put(PROFILE_URL, async ({ request }) => {
        bodies.push(await request.json())

        return new HttpResponse(null, { status: 204 })
      })
    )

    await updateBarberProfile(PROFILE_ID, DRAFT)

    expect(bodies).toHaveLength(1)
    expect(bodies[0]).toMatchObject({
      displayName: DRAFT.displayName,
      latePolicy: expect.any(String),
      additionalServices: DRAFT.additionalServices,
    })
    expect(bodies[0]).not.toHaveProperty('grade')
  })
})

function apiError(status: number, codes: string[] = []): ApiError {
  return new ApiError(status, { status, errorCodes: codes }, 'English detail')
}

describe('describeBarberProfileError', () => {
  it('400 профиля — ошибка у поля и русский текст', () => {
    expect(
      describeBarberProfileError(apiError(400, ['barberProfile.displayName']))
    ).toMatchObject({ field: 'displayName', canRetry: false })

    expect(
      describeBarberProfileError(
        apiError(400, ['barberProfile.displayName.tooLong'])
      )
    ).toMatchObject({
      field: 'displayName',
      message: 'Не длиннее 120 символов.',
      canRetry: false,
    })

    expect(
      describeBarberProfileError(
        apiError(400, ['barberProfile.yearsOfExperience'])
      )
    ).toMatchObject({ field: 'yearsOfExperience' })

    expect(
      describeBarberProfileError(apiError(400, ['barberProfile.languages.unknown']))
    ).toMatchObject({ field: 'languages' })

    expect(
      describeBarberProfileError(
        apiError(400, ['barberProfile.paymentMethods.unknown'])
      )
    ).toMatchObject({ field: 'paymentMethods' })
  })

  it('409 duplicate — услуга уже есть в прайсе', () => {
    expect(
      describeBarberProfileError(
        apiError(409, ['barberProfile.addService.duplicate'])
      )
    ).toMatchObject({
      field: null,
      message: 'Эта услуга уже есть в прайсе.',
      canRetry: false,
    })
  })

  it('404 обеих ручек — услуга уже скрыта, нужна перезагрузка', () => {
    expect(
      describeBarberProfileError(
        apiError(404, ['barberProfile.updateService.notFound'])
      )
    ).toMatchObject({
      message: 'Эта услуга уже скрыта. Обновите страницу.',
      canRetry: false,
    })

    expect(
      describeBarberProfileError(
        apiError(404, ['barberProfile.removeService.notFound'])
      )
    ).toMatchObject({
      message: 'Эта услуга уже скрыта. Обновите страницу.',
      canRetry: false,
    })
  })

  it('400 цены и длительности — тексты полей услуги', () => {
    expect(
      describeBarberProfileError(apiError(400, ['barberService.updatePrice']))
    ).toMatchObject({
      field: null,
      message: 'Цена не может быть отрицательной.',
      canRetry: false,
    })

    expect(
      describeBarberProfileError(apiError(400, ['barberService.updateDuration']))
    ).toMatchObject({ message: 'Длительность — от 1 до 1440 минут.' })

    expect(
      describeBarberProfileError(apiError(400, ['barberProfile.addService.duration']))
    ).toMatchObject({ message: 'Длительность — от 1 до 1440 минут.' })
  })

  it('403 — прайс доступен только владельцу профиля', () => {
    expect(describeBarberProfileError(apiError(403))).toMatchObject({
      field: null,
      message: 'Прайс доступен только владельцу профиля.',
      canRetry: false,
    })

    expect(
      describeBarberProfileError(apiError(403, ['auth.forbidden']))
    ).toMatchObject({
      message: 'Прайс доступен только владельцу профиля.',
    })
  })

  it('429 и обрыв сети можно повторить', () => {
    expect(describeBarberProfileError(apiError(429))).toMatchObject({
      field: null,
      canRetry: true,
      message: 'Слишком много запросов. Подождите минуту и повторите.',
    })

    expect(describeBarberProfileError(apiError(0))).toMatchObject({
      field: null,
      canRetry: true,
      message:
        'Нет связи с сервером. Проверьте подключение и повторите попытку.',
    })
  })

  it('5xx повторяем с текстом правки, неизвестный отказ — нет', () => {
    expect(describeBarberProfileError(apiError(500))).toMatchObject({
      field: null,
      message: 'Не удалось сохранить профиль. Попробуйте ещё раз.',
      canRetry: true,
    })

    expect(
      describeBarberProfileError(apiError(400, ['barberProfile.unknown']))
    ).toMatchObject({
      field: null,
      message: 'Не удалось сохранить профиль. Попробуйте ещё раз.',
      canRetry: false,
    })

    expect(describeBarberProfileError(new Error('boom'))).toMatchObject({
      field: null,
      message: 'Не удалось сохранить профиль. Попробуйте ещё раз.',
      canRetry: false,
    })
  })
})
