import { http, HttpResponse, type JsonBodyType } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  describeAdminSummaryError,
  fetchAdminSummary,
  formatAdminBadgeCount,
  selectSectionCounts,
} from '@/lib/admin-summary'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Транспорт, строгий guard и селекторы админской сводки.
 *
 * Контракт `GET /api/admin/summary`: параметров нет вообще — ни `status`, ни
 * `pageSize`, ни `cursor`. Сводка — единственное агрегатное чтение админки, и её
 * тело проверяется строго: неотрицательные целые счётчики, семь обязательных
 * блоков и `oldest` либо `null`, либо компактная строка очереди.
 */

const SUMMARY_URL = '/api/admin/summary'

const OLDEST = {
  id: '9e800000-0000-4000-8000-000000000001',
  rating: 4,
  awaitingSince: '2026-09-10T10:00:00+00:00',
  barberDisplayName: 'E2E Барбер',
  customerDisplayName: 'Иван Петров',
}

const SUMMARY = {
  reviews: { pendingCount: 14, oldest: OLDEST },
  barbers: { unverifiedCount: 1 },
  salons: { inactiveCount: 1, pendingModerationCount: 2 },
  photos: { pendingAvatarsCount: 2, pendingPortfolioCount: 3 },
  users: { unverifiedCount: 4 },
  barberRequests: { pendingCount: 5 },
  bookings: { confirmedCount: 6 },
}

/** Тело ответа, которое должно собирать ошибку строгого guard'а. */
const INVALID_SUMMARIES: readonly [string, JsonBodyType][] = [
  ['отрицательный счётчик', {
    ...SUMMARY,
    reviews: { pendingCount: -1, oldest: null },
  }],
  ['дробный счётчик', {
    ...SUMMARY,
    barbers: { unverifiedCount: 1.5 },
  }],
  ['отсутствующий блок', {
    reviews: { pendingCount: 0, oldest: null },
    barbers: { unverifiedCount: 0 },
    photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 0 },
    users: { unverifiedCount: 0 },
    barberRequests: { pendingCount: 0 },
    bookings: { confirmedCount: 0 },
  }],
  ['отсутствующий блок photos', {
    reviews: { pendingCount: 0, oldest: null },
    barbers: { unverifiedCount: 0 },
    salons: { inactiveCount: 0, pendingModerationCount: 0 },
    users: { unverifiedCount: 0 },
    barberRequests: { pendingCount: 0 },
    bookings: { confirmedCount: 0 },
  }],
  ['отсутствующий блок users', {
    reviews: { pendingCount: 0, oldest: null },
    barbers: { unverifiedCount: 0 },
    salons: { inactiveCount: 0, pendingModerationCount: 0 },
    photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 0 },
    barberRequests: { pendingCount: 0 },
    bookings: { confirmedCount: 0 },
  }],
  ['отсутствующий блок barberRequests', {
    reviews: { pendingCount: 0, oldest: null },
    barbers: { unverifiedCount: 0 },
    salons: { inactiveCount: 0, pendingModerationCount: 0 },
    photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 0 },
    users: { unverifiedCount: 0 },
    bookings: { confirmedCount: 0 },
  }],
  ['отсутствующий блок bookings', {
    reviews: { pendingCount: 0, oldest: null },
    barbers: { unverifiedCount: 0 },
    salons: { inactiveCount: 0, pendingModerationCount: 0 },
    photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 0 },
    users: { unverifiedCount: 0 },
    barberRequests: { pendingCount: 0 },
  }],
  ['салоны без pendingModerationCount', {
    ...SUMMARY,
    salons: { inactiveCount: 0 },
  }],
  ['дробный счётчик фото', {
    ...SUMMARY,
    photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 2.5 },
  }],
  ['«самый старый» без поля', {
    ...SUMMARY,
    reviews: {
      pendingCount: 1,
      oldest: { id: OLDEST.id, rating: 4, awaitingSince: OLDEST.awaitingSince },
    },
  }],
  ['оценка вне 1..5', {
    ...SUMMARY,
    reviews: { pendingCount: 1, oldest: { ...OLDEST, rating: 6 } },
  }],
]

function serveSummary(body: JsonBodyType) {
  server.use(http.get(SUMMARY_URL, () => HttpResponse.json(body)))
}

describe('fetchAdminSummary', () => {
  it('запрашивает ровно GET /api/admin/summary без параметров', async () => {
    const requested: { url: string; method: string }[] = []

    server.use(
      http.get(SUMMARY_URL, ({ request }) => {
        requested.push({ url: request.url, method: request.method })

        return HttpResponse.json(SUMMARY)
      })
    )

    await expect(fetchAdminSummary()).resolves.toStrictEqual(SUMMARY)

    expect(requested).toHaveLength(1)
    expect(requested[0].method).toBe('GET')
    expect(new URL(requested[0].url).pathname).toBe(SUMMARY_URL)
    expect(new URL(requested[0].url).search).toBe('')
  })

  it('принимает пустую очередь и лишние поля ответа', async () => {
    serveSummary({
      ...SUMMARY,
      reviews: { pendingCount: 0, oldest: null },
      total: 14,
    })

    await expect(fetchAdminSummary()).resolves.toStrictEqual({
      ...SUMMARY,
      reviews: { pendingCount: 0, oldest: null },
      total: 14,
    })
  })
})

describe('строгий guard сводки', () => {
  for (const [name, body] of INVALID_SUMMARIES) {
    it(`не пропускает ответ: ${name}`, async () => {
      serveSummary(body)

      await expect(fetchAdminSummary()).rejects.toBeInstanceOf(ApiError)
      await expect(fetchAdminSummary()).rejects.toThrow(
        'Сервер вернул неожиданный ответ.'
      )
    })
  }
})

describe('selectSectionCounts', () => {
  it('раскладывает снимок по пунктам рейла и складывает очереди фото', () => {
    expect(selectSectionCounts(SUMMARY)).toStrictEqual({
      reviews: 14,
      photos: 5,
      photosAvatars: 2,
      photosPortfolio: 3,
      barbers: 1,
      users: 4,
      barberRequests: 5,
      salons: 2,
      bookings: 6,
    })
  })

  it('ноль остаётся нулём: сумму не выдумывает', () => {
    expect(
      selectSectionCounts({
        ...SUMMARY,
        reviews: { pendingCount: 0, oldest: null },
        salons: { inactiveCount: 3, pendingModerationCount: 0 },
      })
    ).toStrictEqual({
      reviews: 0,
      photos: 5,
      photosAvatars: 2,
      photosPortfolio: 3,
      barbers: 1,
      users: 4,
      barberRequests: 5,
      salons: 0,
      bookings: 6,
    })
  })
})

describe('formatAdminBadgeCount', () => {
  it('оставляет двузначные числа и капит сотню', () => {
    expect(formatAdminBadgeCount(1)).toBe('1')
    expect(formatAdminBadgeCount(12)).toBe('12')
    expect(formatAdminBadgeCount(99)).toBe('99')
    expect(formatAdminBadgeCount(100)).toBe('99+')
    expect(formatAdminBadgeCount(4321)).toBe('99+')
  })
})

describe('describeAdminSummaryError', () => {
  const fallback = 'Не удалось загрузить сводку.'

  it('403 объясняет роль', () => {
    const error = new ApiError(403, problemDetails(403), 'Forbidden.')

    expect(describeAdminSummaryError(error, fallback)).toBe(
      'Раздел доступен только администратору.'
    )
  })

  it('429 просит подождать минуту', () => {
    const error = new ApiError(429, problemDetails(429), 'Too many.')

    expect(describeAdminSummaryError(error, fallback)).toBe(
      'Слишком много запросов. Подождите минуту и повторите.'
    )
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(describeAdminSummaryError(new Error('сеть'), fallback)).toBe(fallback)
  })
})
