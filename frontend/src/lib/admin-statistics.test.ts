import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  describeAdminStatisticsError,
  defaultStatisticsPeriod,
  fetchAdminStatistics,
  formatStatisticsRange,
  isEmptyStatistics,
  isStatisticsDay,
  parseStatisticsPeriod,
  resolveStatisticsPeriod,
  STATISTICS_MAX_DAYS,
  statisticsDays,
  statisticsPath,
  statisticsPresetPeriod,
  todayUtc,
  type AdminStatistics,
} from '@/lib/admin-statistics'
import { isAdminStatistics } from '@/lib/response-guards'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { statisticsFixture } from '@/test/statistics'

const PERIOD = { from: '2026-10-10', to: '2026-11-08', compare: false }

describe('admin-statistics', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('читает период и не добавляет compare без сравнения', async () => {
    const requests: string[] = []

    server.use(
      http.get('/api/admin/statistics', ({ request }) => {
        requests.push(request.url)

        return HttpResponse.json(statisticsFixture())
      })
    )

    const statistics = await fetchAdminStatistics(PERIOD)

    expect(statistics.overview.completedVisits.value).toBe(128)
    expect(requests).toHaveLength(1)

    const url = new URL(requests[0])

    expect(url.searchParams.get('from')).toBe('2026-10-10')
    expect(url.searchParams.get('to')).toBe('2026-11-08')
    expect(url.searchParams.get('compare')).toBeNull()
  })

  it('передаёт compare=true при включённом сравнении', async () => {
    let requested = ''

    server.use(
      http.get('/api/admin/statistics', ({ request }) => {
        requested = request.url

        return HttpResponse.json(statisticsFixture())
      })
    )

    await fetchAdminStatistics({ ...PERIOD, compare: true })

    expect(new URL(requested).searchParams.get('compare')).toBe('true')
  })

  it('отвергает битое тело и принимает лишние поля', async () => {
    const body = statisticsFixture()

    expect(isAdminStatistics(body)).toBe(true)
    expect(isAdminStatistics({ ...body, extra: 'ok' })).toBe(true)
    expect(
      isAdminStatistics({
        ...body,
        overview: { ...body.overview, completedVisits: { value: 1.5 } },
      })
    ).toBe(false)
    expect(
      isAdminStatistics({
        ...body,
        clients: { ...body.clients, guestShare: 1.5 },
      })
    ).toBe(false)
    expect(
      isAdminStatistics({
        ...body,
        series: { granularity: 'day', points: body.series.points.slice(0, 2) },
      })
    ).toBe(false)
    expect(
      isAdminStatistics({
        ...body,
        quality: { ...body.quality, distribution: [0, 1, 2, 5] },
      })
    ).toBe(false)
    expect(
      isAdminStatistics({
        ...body,
        top: {
          ...body.top,
          barbers: { rows: [], other: { visits: -1, turnover: 0 } },
        },
      })
    ).toBe(false)

    server.use(
      http.get('/api/admin/statistics', () =>
        HttpResponse.json({ ...body, period: { from: '2026-10-10' } })
      )
    )

    await expect(fetchAdminStatistics(PERIOD)).rejects.toBeInstanceOf(ApiError)
  })

  it('маппит 403, 429, 400 и неизвестную ошибку в русский текст', () => {
    const forbidden = new ApiError(
      403,
      problemDetails(403, { errorCodes: ['auth.forbidden'] }),
      'Forbidden'
    )
    const tooMany = new ApiError(429, problemDetails(429), 'Too many')
    const badPeriod = new ApiError(
      400,
      problemDetails(400, { errorCodes: ['admin.statistics.period'] }),
      'Bad period'
    )
    const unknown = new ApiError(500, problemDetails(500), 'Boom')

    expect(describeAdminStatisticsError(forbidden, 'fallback')).toBe(
      'Раздел доступен только администратору.'
    )
    expect(describeAdminStatisticsError(tooMany, 'fallback')).toBe(
      'Слишком много запросов. Подождите минуту и повторите.'
    )
    expect(describeAdminStatisticsError(badPeriod, 'fallback')).toBe(
      'Проверьте период: максимум — 366 дней.'
    )
    expect(describeAdminStatisticsError(unknown, 'fallback')).toBe('fallback')
    expect(describeAdminStatisticsError(new Error('x'), 'fallback')).toBe(
      'fallback'
    )
  })

  it('разбирает период из адреса и откатывается на 30 дней', () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-08T09:30:00Z'))

    expect(todayUtc()).toBe('2026-11-08')
    expect(defaultStatisticsPeriod()).toEqual({
      from: '2026-10-10',
      to: '2026-11-08',
      compare: false,
    })
    expect(statisticsPresetPeriod(7)).toEqual({
      from: '2026-11-02',
      to: '2026-11-08',
      compare: false,
    })

    expect(
      parseStatisticsPeriod('?from=2026-10-10&to=2026-11-08&compare=1')
    ).toEqual({
      from: '2026-10-10',
      to: '2026-11-08',
      compare: true,
    })
    expect(parseStatisticsPeriod('?from=bad&to=2026-11-08')).toEqual({
      from: '2026-10-10',
      to: '2026-11-08',
      compare: false,
    })
    expect(parseStatisticsPeriod('?from=2026-11-08&to=2026-10-10')).toEqual({
      from: '2026-10-10',
      to: '2026-11-08',
      compare: false,
    })
    expect(parseStatisticsPeriod('?from=2020-01-01&to=2026-11-08')).toEqual({
      from: '2026-10-10',
      to: '2026-11-08',
      compare: false,
    })

    expect(resolveStatisticsPeriod('2026-10-10', '2026-11-08', true)).toEqual({
      from: '2026-10-10',
      to: '2026-11-08',
      compare: true,
    })
  })

  it('считает дни включительно и проверяет ISO-дату', () => {
    expect(statisticsDays('2026-10-10', '2026-11-08')).toBe(30)
    expect(statisticsDays('2026-11-08', '2026-10-10')).toBeNull()
    expect(isStatisticsDay('2026-02-30')).toBe(false)
    expect(isStatisticsDay('2026-11-08')).toBe(true)
    expect(STATISTICS_MAX_DAYS).toBe(366)
  })

  it('строит адрес с периодом и сравнением', () => {
    expect(statisticsPath(PERIOD)).toBe(
      '/admin/statistics?from=2026-10-10&to=2026-11-08'
    )
    expect(statisticsPath({ ...PERIOD, compare: true })).toBe(
      '/admin/statistics?from=2026-10-10&to=2026-11-08&compare=1'
    )
  })

  it('подписывает период без лишнего года и со сравнением', () => {
    expect(formatStatisticsRange(PERIOD)).toBe('10 окт. — 8 нояб. 2026')
    expect(
      formatStatisticsRange({
        from: '2025-12-20',
        to: '2026-01-05',
        compare: false,
      })
    ).toBe('20 дек. 2025 — 5 янв. 2026')
  })

  it('считает пустой период по созданным записям и заявкам', () => {
    const body = statisticsFixture()

    expect(isEmptyStatistics(body)).toBe(false)

    const empty: AdminStatistics = {
      ...body,
      overview: {
        ...body.overview,
        completedVisits: { value: 0, previous: null },
        turnover: { value: 0, previous: null, currency: 'RUB' },
        newAccounts: { value: 0, previous: null },
      },
      clients: {
        ...body.clients,
        guestBookings: 0,
      },
      quality: {
        ...body.quality,
        rating: null,
        reviews: 0,
        publishedShare: null,
        requests: {
          submitted: 0,
          approved: 0,
          rejected: 0,
          approvalRate: null,
          medianDecisionHours: null,
        },
        salons: {
          submitted: 0,
          approved: 0,
          rejected: 0,
          approvalRate: null,
          medianDecisionHours: null,
        },
      },
      supply: { ...body.supply, newProfiles: 0 },
      top: {
        barbers: { rows: [], other: { visits: 0, turnover: 0 } },
        services: { rows: [], other: { visits: 0, turnover: 0 } },
        cities: { rows: [], other: { visits: 0, turnover: 0 } },
      },
      series: {
        granularity: 'day',
        points: body.series.points.map((point) => ({
          ...point,
          created: 0,
          completed: 0,
          cancelled: 0,
          noShow: 0,
        })),
      },
    }

    expect(isEmptyStatistics(empty)).toBe(true)
  })
})
