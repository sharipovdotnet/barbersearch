import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  attachSalon,
  createOwnSalon,
  describeSalonAttachError,
  fetchBarberProfileSalons,
  isSalonAttachStale,
} from '@/lib/barber-salons'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const LIST_URL = `/api/barber-profiles/${PROFILE_ID}/salons`

const OWNER_ID = '33333333-3333-4333-8333-333333333333'

const SUMMARY = {
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon',
  isListed: true,
  isPrimary: true,
  country: 'Russia',
  postalCode: '620000',
  latitude: 56.8389,
  longitude: 60.6057,
  phoneNumber: '+73430000000',
  websiteUrl: 'https://barbershop.example',
  ownerUserId: OWNER_ID,
  moderationStatus: 'Active',
  rejectionReason: null,
}

describe('fetchBarberProfileSalons', () => {
  it('читает список и игнорирует лишние поля', async () => {
    server.use(
      http.get(LIST_URL, () => HttpResponse.json([{ ...SUMMARY, extra: 1 }]))
    )

    const salons = await fetchBarberProfileSalons(PROFILE_ID)

    expect(salons).toHaveLength(1)
    expect(salons[0]).toMatchObject(SUMMARY)
  })

  it('не прячет салон на проверке и его причину отказа', async () => {
    server.use(
      http.get(LIST_URL, () =>
        HttpResponse.json([
          {
            ...SUMMARY,
            moderationStatus: 'Rejected',
            rejectionReason: 'Нет подтверждения адреса',
          },
        ])
      )
    )

    const [salon] = await fetchBarberProfileSalons(PROFILE_ID)

    expect(salon?.moderationStatus).toBe('Rejected')
    expect(salon?.rejectionReason).toBe('Нет подтверждения адреса')
  })

  it('отвергает ответ без координат и владельца: контракт расширен', async () => {
    const oldSummary = {
      salonId: SUMMARY.salonId,
      name: SUMMARY.name,
      city: SUMMARY.city,
      address: SUMMARY.address,
      timeZoneId: SUMMARY.timeZoneId,
      isPrimary: SUMMARY.isPrimary,
      moderationStatus: SUMMARY.moderationStatus,
      rejectionReason: SUMMARY.rejectionReason,
    }

    server.use(http.get(LIST_URL, () => HttpResponse.json([oldSummary])))

    await expect(fetchBarberProfileSalons(PROFILE_ID)).rejects.toBeInstanceOf(
      ApiError
    )
  })
})

describe('createOwnSalon', () => {
  const input = {
    name: 'E2E Studio Проверка',
    address: 'ул. Проверочная, 5',
    city: 'Москва',
    country: 'Russia',
    timeZoneId: 'Europe/Moscow',
    latitude: 55.75,
    longitude: 37.62,
    postalCode: null,
    phoneNumber: '+70000000055',
    websiteUrl: 'https://e2e-salon.example',
    kind: 'Salon' as const,
    isListed: true,
  }

  it('отправляет POST /api/salons/own с CSRF, cookie и полным телом', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    const calls: { body: unknown; csrfToken: string | null }[] = []

    server.use(
      http.post('/api/salons/own', async ({ request }) => {
        calls.push({
          body: await request.json(),
          csrfToken: request.headers.get('X-CSRF-TOKEN'),
        })

        return HttpResponse.json({ id: SALON_ID }, { status: 201 })
      })
    )

    await expect(createOwnSalon(input)).resolves.toStrictEqual({
      id: SALON_ID,
    })

    expect(csrfCalls()).toBe(1)
    expect(calls).toStrictEqual([
      {
        body: input,
        csrfToken: 'csrf-token',
      },
    ])

    // Cookie-сессия: запрос уходит с `credentials: 'include'` (MSW нормализует
    // значение в Request, поэтому проверяем инициатора запроса).
    const ownCall = fetchSpy.mock.calls.find(([url]) =>
      String(url).includes('/api/salons/own')
    )

    expect(ownCall?.[1]).toMatchObject({
      method: 'POST',
      credentials: 'include',
    })
  })

  it('барбер без профиля получает 403 barberProfile.notFound', async () => {
    server.use(
      http.post('/api/salons/own', () =>
        HttpResponse.json(
          problemDetails(403, { errorCodes: ['barberProfile.notFound'] }),
          { status: 403 }
        )
      )
    )

    await expect(createOwnSalon(input)).rejects.toMatchObject({ status: 403 })
  })
})

describe('attachSalon', () => {
  it('отправляет только salonId и isPrimary, ждёт 204', async () => {
    let body: unknown = null

    server.use(
      http.post(LIST_URL, async ({ request }) => {
        body = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await attachSalon(PROFILE_ID, { salonId: SALON_ID, isPrimary: true })

    expect(body).toEqual({ salonId: SALON_ID, isPrimary: true })
  })
})

function apiError(status: number, codes: string[] = []): ApiError {
  return new ApiError(status, { status, errorCodes: codes }, 'failure')
}

describe('describeSalonAttachError', () => {
  it('переводит публичные коды на русский', () => {
    expect(
      describeSalonAttachError(
        apiError(409, ['barberProfile.addSalon.duplicate'])
      )
    ).toBe('Этот салон уже добавлен.')

    expect(
      describeSalonAttachError(
        apiError(404, ['barberProfile.addSalon.salonNotFound'])
      )
    ).toBe('Салон больше не доступен.')

    expect(describeSalonAttachError(apiError(403))).toBe(
      'Нет доступа к профилю.'
    )
    expect(describeSalonAttachError(apiError(401))).toBe(
      'Сессия истекла — войдите снова.'
    )
    expect(describeSalonAttachError(apiError(429))).toBe(
      'Слишком много запросов. Подождите минуту и повторите.'
    )
  })

  it('409/404 считает выбор устаревшим, 403/429 — нет', () => {
    expect(
      isSalonAttachStale(apiError(409, ['barberProfile.addSalon.duplicate']))
    ).toBe(true)
    expect(
      isSalonAttachStale(apiError(404, ['barberProfile.addSalon.salonNotFound']))
    ).toBe(true)
    expect(isSalonAttachStale(apiError(403))).toBe(false)
    expect(isSalonAttachStale(apiError(429))).toBe(false)
  })
})
