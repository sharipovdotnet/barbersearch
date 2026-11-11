import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import {
  BARBER_SEARCH_PAGE_SIZE,
  emptyBarberSearchFilters,
  normalizeBarberSearchFilters,
  searchBarbers,
  type BarberSearchItem,
} from '@/lib/barber-search'
import { server } from '@/test/msw/server'

const ITEM: BarberSearchItem = {
  id: '11111111-1111-4111-8111-111111111111',
  displayName: 'Тимур Барберов',
  grade: 'SeniorBarber',
  yearsOfExperience: 8,
  languages: ['Ru'],
  rating: null,
  reviewsCount: 0,
  isVerified: true,
  primarySalonId: null,
  primarySalonName: null,
  primaryPlaceKind: null,
  city: null,
  additionalServices: ['Бритьё головы', 'Уход за бородой'],
  avatarUrl: null,
}

/** Фильтры с одним изменённым полем — остальные остаются «без фильтра». */
function filters(additionalService: string) {
  return {
    ...emptyBarberSearchFilters(),
    additionalService,
  }
}

describe('normalizeBarberSearchFilters', () => {
  it('обрезает город и дополнительную услугу, остальное не трогает', () => {
    expect(
      normalizeBarberSearchFilters({
        city: '  Москва  ',
        date: '2026-12-01',
        specializationId: 'specialization-id',
        hairServiceId: 'hair-service-id',
        language: 'Ru',
        additionalService: '  борода  ',
      })
    ).toStrictEqual({
      city: 'Москва',
      date: '2026-12-01',
      specializationId: 'specialization-id',
      hairServiceId: 'hair-service-id',
      language: 'Ru',
      additionalService: 'борода',
    })
  })
})

describe('searchBarbers, дополнительная услуга', () => {
  it('уходит подстрокой в query и не дублирует пустые фильтры', async () => {
    let requestUrl = ''

    server.use(
      http.get('/api/barbers', ({ request }) => {
        requestUrl = request.url

        return HttpResponse.json({ items: [ITEM], nextCursor: null })
      })
    )

    const result = await searchBarbers(
      filters('  бород  '),
      null,
      new AbortController().signal
    )

    const query = new URL(requestUrl).searchParams

    expect(query.get('pageSize')).toBe(String(BARBER_SEARCH_PAGE_SIZE))
    expect(query.get('additionalService')).toBe('бород')
    expect(query.has('city')).toBe(false)
    expect(query.has('date')).toBe(false)
    expect(query.has('specializationId')).toBe(false)
    expect(query.has('hairServiceId')).toBe(false)
    expect(query.has('language')).toBe(false)
    expect(result.items[0]?.additionalServices).toStrictEqual([
      'Бритьё головы',
      'Уход за бородой',
    ])
  })

  it('пустая подстрока не попадает в запрос', async () => {
    let requestUrl = ''

    server.use(
      http.get('/api/barbers', ({ request }) => {
        requestUrl = request.url

        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    await searchBarbers(filters('   '), null, new AbortController().signal)

    expect(new URL(requestUrl).searchParams.has('additionalService')).toBe(
      false
    )
  })
})
