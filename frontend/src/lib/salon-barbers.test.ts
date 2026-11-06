import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  attachSalonBarber,
  describeSalonBarberError,
  detachSalonBarber,
  fetchSalonBarbers,
  isSalonBarberActionStale,
  searchSalonBarberCandidates,
} from '@/lib/salon-barbers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const SALON_ID = '22222222-2222-4222-8222-222222222222'
const BARBER_ID = '33333333-3333-4333-8333-333333333333'
const USER_ID = '44444444-4444-4444-8444-444444444444'
const LIST_URL = `/api/salons/${SALON_ID}/barbers`

const BARBER = {
  barberProfileId: BARBER_ID,
  userId: USER_ID,
  displayName: 'Тимур Барберов',
  email: 'timur@barbersearch.test',
  isActive: true,
  isVerified: true,
}

describe('fetchSalonBarbers', () => {
  it('читает состав салона и игнорирует лишние поля', async () => {
    server.use(
      http.get(LIST_URL, () =>
        HttpResponse.json([{ ...BARBER, extra: 'ignored' }])
      )
    )

    const rows = await fetchSalonBarbers(SALON_ID)

    expect(rows).toHaveLength(1)
    expect(rows[0]).toMatchObject(BARBER)
  })

  it('отвергает строку без userId: контракт состава расширен', async () => {
    const oldRow = {
      barberProfileId: BARBER.barberProfileId,
      displayName: BARBER.displayName,
      email: BARBER.email,
      isActive: BARBER.isActive,
      isVerified: BARBER.isVerified,
    }

    server.use(http.get(LIST_URL, () => HttpResponse.json([oldRow])))

    await expect(fetchSalonBarbers(SALON_ID)).rejects.toBeInstanceOf(ApiError)
  })
})

describe('searchSalonBarberCandidates', () => {
  it('передаёт строку поиска в query и валидирует ответ', async () => {
    let search: string | null = null

    server.use(
      http.get(`/api/salons/${SALON_ID}/barber-candidates`, ({ request }) => {
        search = new URL(request.url).searchParams.get('search')

        return HttpResponse.json([BARBER])
      })
    )

    const rows = await searchSalonBarberCandidates(SALON_ID, 'Тимур')

    expect(search).toBe('Тимур')
    expect(rows).toEqual([BARBER])
  })
})

describe('attachSalonBarber and detachSalonBarber', () => {
  it('прикрепляет профиль через POST с CSRF и телом barberProfileId', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let body: unknown = null
    let csrfToken: string | null = null

    server.use(
      http.post(LIST_URL, async ({ request }) => {
        body = await request.json()
        csrfToken = request.headers.get('X-CSRF-TOKEN')

        return new HttpResponse(null, { status: 204 })
      })
    )

    await attachSalonBarber(SALON_ID, BARBER_ID)

    expect(body).toEqual({ barberProfileId: BARBER_ID })
    expect(csrfToken).toBe('csrf-token')
    expect(csrfCalls()).toBe(1)
  })

  it('открепляет профиль через DELETE от имени салона', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let method: string | null = null

    server.use(
      http.delete(`${LIST_URL}/${BARBER_ID}`, ({ request }) => {
        method = request.method

        return new HttpResponse(null, { status: 204 })
      })
    )

    await detachSalonBarber(SALON_ID, BARBER_ID)

    expect(method).toBe('DELETE')
    expect(csrfCalls()).toBe(1)
  })
})

function apiError(status: number, codes: string[] = []): ApiError {
  return new ApiError(status, { status, errorCodes: codes }, 'failure')
}

describe('describeSalonBarberError', () => {
  it('переводит публичные коды состава салона на русский', () => {
    expect(
      describeSalonBarberError(
        apiError(409, ['barberProfile.addSalon.duplicate'])
      )
    ).toBe('Этот барбер уже работает в салоне.')

    expect(
      describeSalonBarberError(apiError(409, ['salon.barbers.inactive']))
    ).toBe('Профиль барбера выключен: его нельзя прикрепить к салону.')

    expect(
      describeSalonBarberError(apiError(409, ['salon.barbers.owner']))
    ).toBe('Владельца салона нельзя убрать из его салона.')

    expect(
      describeSalonBarberError(
        apiError(404, ['barberProfile.removeSalon.notFound'])
      )
    ).toBe('Этот барбер уже не работает в салоне.')

    expect(describeSalonBarberError(apiError(403))).toBe(
      'Нет доступа к салону.'
    )
    expect(describeSalonBarberError(apiError(401))).toBe(
      'Сессия истекла — войдите снова.'
    )
  })

  it('409/404 считает список устаревшим, 403/429 — нет', () => {
    expect(isSalonBarberActionStale(apiError(409))).toBe(true)
    expect(isSalonBarberActionStale(apiError(404))).toBe(true)
    expect(isSalonBarberActionStale(apiError(403))).toBe(false)
    expect(isSalonBarberActionStale(apiError(429))).toBe(false)
  })
})
