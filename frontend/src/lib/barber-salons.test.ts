import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  attachSalon,
  describeSalonAttachError,
  fetchBarberProfileSalons,
  isSalonAttachStale,
} from '@/lib/barber-salons'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const LIST_URL = `/api/barber-profiles/${PROFILE_ID}/salons`

const SUMMARY = {
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  isPrimary: true,
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
