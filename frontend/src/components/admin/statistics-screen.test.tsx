import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse, type JsonBodyType } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { StatisticsScreen } from '@/components/admin/statistics-screen'
import type { AdminStatistics } from '@/lib/admin-statistics'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'
import { statisticsFixture } from '@/test/statistics'

/**
 * Экран статистики: плитки, период в URL, сравнение, пустота, ошибки и таблица.
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания экрана, которых не видит
 * unit-тест контракта: значения из ответа, смену периода новым запросом, сравнение
 * с прошлым периодом, малые N абсолютными числами, `401`-гейт и русские ошибки.
 */

const ENDPOINT = '/api/admin/statistics'

const BASE_QUERY = '?from=2026-10-10&to=2026-11-08'

function mockStatistics(body: JsonBodyType | (() => JsonBodyType)): string[] {
  const requests: string[] = []

  server.use(
    http.get(ENDPOINT, ({ request }) => {
      requests.push(request.url)

      return HttpResponse.json(typeof body === 'function' ? body() : body)
    })
  )

  return requests
}

function emptyStatistics(): AdminStatistics {
  const body = statisticsFixture()

  return {
    ...body,
    overview: {
      ...body.overview,
      completedVisits: { value: 0, previous: null },
      turnover: { value: 0, previous: null, currency: 'RUB' },
      averageCheck: { value: null, previous: null },
      newAccounts: { value: 0, previous: null },
      noShowRate: { value: null, previous: null, count: 0, sample: 0 },
      stuckBookings: { value: 0 },
    },
    statuses: {
      pending: 0,
      confirmed: 0,
      completed: 0,
      cancelled: 0,
      noShow: 0,
      due: 0,
      closed: 0,
    },
    clients: {
      withVisit: 0,
      returning: 0,
      firstVisit: 0,
      guestBookings: 0,
      guestShare: null,
      reviewRate: null,
    },
    supply: {
      ...body.supply,
      activeBarbers: 0,
      workingBarbers: 0,
      idleBarbers: 0,
      newProfiles: 0,
      salonsByKind: [],
    },
    top: {
      barbers: { rows: [], other: { visits: 0, turnover: 0 } },
      services: { rows: [], other: { visits: 0, turnover: 0 } },
      cities: { rows: [], other: { visits: 0, turnover: 0 } },
    },
    quality: {
      rating: null,
      reviews: 0,
      distribution: [0, 0, 0, 0, 0],
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
}

describe('StatisticsScreen', () => {
  beforeEach(() => {
    window.history.replaceState(
      null,
      '',
      `/admin/statistics${BASE_QUERY}`
    )
  })

  afterEach(() => {
    window.history.replaceState(null, '', '/')
    vi.useRealTimers()
  })

  it('показывает плитки, секции, лидеров и «Как считаем»', async () => {
    mockStatistics(statisticsFixture())

    render(<StatisticsScreen />)

    expect(
      screen.getByRole('heading', { name: 'Статистика', level: 1 })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Что происходит за период: записи, клиенты, предложение и качество.'
      )
    ).toBeInTheDocument()

    expect(await screen.findByText('128 визитов')).toBeInTheDocument()
    expect(screen.getByText(/284\s*500/)).toBeInTheDocument()
    expect(screen.getByText(/2\s*222,66/)).toBeInTheDocument()
    expect(screen.getByText('42 аккаунта')).toBeInTheDocument()
    expect(screen.getByText('4,7 %')).toBeInTheDocument()
    expect(screen.getByText('7 броней')).toBeInTheDocument()

    expect(
      screen.getByRole('region', { name: 'Обзор' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('region', { name: 'Записи' })
    ).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Клиенты' })).toBeInTheDocument()
    expect(
      screen.getByRole('region', { name: 'Предложение' })
    ).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Лидеры' })).toBeInTheDocument()
    expect(
      screen.getByRole('region', { name: 'Качество и модерация' })
    ).toBeInTheDocument()

    expect(
      screen.getByRole('img', { name: /Записи и визиты по дням/ })
    ).toBeInTheDocument()
    expect(screen.getByText('143 из 150')).toBeInTheDocument()

    const top = screen.getByRole('region', { name: 'Лидеры' })

    expect(
      within(top).getByRole('link', { name: 'Артём Соколов' })
    ).toHaveAttribute(
      'href',
      '/admin/barbers/9e800000-0000-4000-8000-000000000001?status=all'
    )
    expect(within(top).getAllByText('Остальные')).toHaveLength(3)
    expect(within(top).getByText('Москва')).toBeInTheDocument()

    const quality = screen.getByRole('region', { name: 'Качество и модерация' })

    expect(within(quality).getByText('Только опубликованные: 38')).toBeInTheDocument()
    expect(within(quality).getByText('90 %')).toBeInTheDocument()
    expect(within(quality).getByText('26,5 ч')).toBeInTheDocument()

    expect(screen.getByText('Как считаем')).toBeInTheDocument()
    expect(
      screen.getByText(/сумма цен завершённых визитов/)
    ).toBeInTheDocument()
    expect(screen.getByText(/Это не выручка/)).toBeInTheDocument()
  })

  it('меняет период пресетом и делает новый запрос', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-08T09:30:00Z'))

    const user = userEvent.setup()
    const requests = mockStatistics(statisticsFixture())

    render(<StatisticsScreen />)

    await screen.findByText('128 визитов')
    expect(requests).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: '7 дней' }))

    await waitFor(() => {
      expect(requests).toHaveLength(2)
    })

    const url = new URL(requests[1])

    expect(url.searchParams.get('from')).toBe('2026-11-02')
    expect(url.searchParams.get('to')).toBe('2026-11-08')
    expect(window.location.search).toContain('from=2026-11-02')
  })

  it('меняет период двумя датами', async () => {
    const requests = mockStatistics(statisticsFixture())

    render(<StatisticsScreen />)

    await screen.findByText('128 визитов')

    fireEvent.change(screen.getByLabelText('С даты'), {
      target: { value: '2026-09-01' },
    })

    await waitFor(() => {
      expect(requests).toHaveLength(2)
    })

    const url = new URL(requests[1])

    expect(url.searchParams.get('from')).toBe('2026-09-01')
    expect(url.searchParams.get('to')).toBe('2026-11-08')
  })

  it('включает сравнение и показывает дельты прошлого периода', async () => {
    const user = userEvent.setup()
    let compared = false
    const requests = mockStatistics(() =>
      statisticsFixture('2026-10-10', '2026-11-08', compared)
    )

    render(<StatisticsScreen />)

    await screen.findByText('128 визитов')
    expect(screen.queryByText('+12,3 %')).not.toBeInTheDocument()

    compared = true

    await user.click(
      screen.getByRole('checkbox', { name: 'Сравнить с прошлым периодом' })
    )

    await waitFor(() => {
      expect(requests).toHaveLength(2)
    })

    expect(new URL(requests[1]).searchParams.get('compare')).toBe('true')
    expect(await screen.findByText('+12,3 %')).toBeInTheDocument()
    expect(window.location.search).toContain('compare=1')
  })

  it('показывает таблицу-дубль по «Показать таблицей»', async () => {
    const user = userEvent.setup()
    mockStatistics(statisticsFixture())

    render(<StatisticsScreen />)

    await screen.findByText('128 визитов')

    // jsdom не скрывает содержимое закрытого `<details>`, поэтому проверяем
    // сам переключатель и таблицу после клика, а не видимость в браузере.
    await user.click(screen.getByText('Показать таблицей'))

    const table = screen.getByRole('table', {
      name: 'Записи и визиты по дням',
    })

    expect(within(table).getAllByRole('row')).toHaveLength(31)
  })

  it('показывает малые N абсолютными числами', async () => {
    const body = statisticsFixture()

    body.overview.noShowRate = {
      value: 0.5,
      previous: 0.25,
      count: 1,
      sample: 2,
    }

    mockStatistics(body)

    render(<StatisticsScreen />)

    expect(await screen.findByText('1 из 2')).toBeInTheDocument()
    expect(
      within(screen.getByRole('region', { name: 'Обзор' })).queryByText('50 %')
    ).not.toBeInTheDocument()
  })

  it('пустой период называет честно', async () => {
    mockStatistics(emptyStatistics())

    render(<StatisticsScreen />)

    expect(
      await screen.findByText(
        'За этот период данных нет. Попробуйте расширить период.'
      )
    ).toBeInTheDocument()
    expect(screen.getByText('0 визитов')).toBeInTheDocument()
    expect(
      screen.queryByRole('img', { name: /Записи и визиты/ })
    ).not.toBeInTheDocument()
  })

  it('первая ошибка показывает «Повторить», повтор читает снова', async () => {
    const user = userEvent.setup()
    let calls = 0

    server.use(
      http.get(ENDPOINT, () => {
        calls += 1

        return calls === 1
          ? HttpResponse.json(problemDetails(500), { status: 500 })
          : HttpResponse.json(statisticsFixture())
      })
    )

    render(<StatisticsScreen />)

    expect(
      await screen.findByText('Не удалось загрузить статистику')
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(await screen.findByText('128 визитов')).toBeInTheDocument()
    expect(calls).toBe(2)
  })

  it('403 показывает отказ по роли, 429 — лимит, 400 — период', async () => {
    server.use(
      http.get(ENDPOINT, () =>
        HttpResponse.json(problemDetails(403), { status: 403 })
      )
    )

    const { unmount } = render(<StatisticsScreen />)

    expect(
      await screen.findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()

    unmount()

    server.use(
      http.get(ENDPOINT, () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    const second = render(<StatisticsScreen />)

    expect(
      await screen.findByText(
        'Слишком много запросов. Подождите минуту и повторите.'
      )
    ).toBeInTheDocument()

    second.unmount()

    server.use(
      http.get(ENDPOINT, () =>
        HttpResponse.json(
          problemDetails(400, { errorCodes: ['admin.statistics.period'] }),
          { status: 400 }
        )
      )
    )

    render(<StatisticsScreen />)

    expect(
      await screen.findByText('Проверьте период: максимум — 366 дней.')
    ).toBeInTheDocument()
  })

  it('401 показывает гейт входа и не рисует секции', async () => {
    server.use(
      http.get(ENDPOINT, () =>
        HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.required'] }),
          { status: 401 }
        )
      )
    )

    render(<StatisticsScreen />)

    expect(
      await screen.findByText('Вход для администраторов')
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Статистика', level: 1 })
    ).not.toBeInTheDocument()
  })

  it('«Обновить» делает один запрос и не стирает секции', async () => {
    const user = userEvent.setup()
    const requests = mockStatistics(statisticsFixture())

    render(<StatisticsScreen />)

    await screen.findByText('128 визитов')
    expect(requests).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Обновить' }))

    await waitFor(() => {
      expect(requests).toHaveLength(2)
    })

    expect(screen.getByText('128 визитов')).toBeInTheDocument()
    expect(screen.getByText(/Обновлено в \d{2}:\d{2}/)).toBeInTheDocument()
  })

  it('некорректный адрес открывает дефолт в 30 дней', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-11-08T09:30:00Z'))
    window.history.replaceState(null, '', '/admin/statistics?from=bad&to=2026-11-08')

    const requests = mockStatistics(statisticsFixture('2026-10-10', '2026-11-08'))

    render(<StatisticsScreen />)

    await screen.findByText('128 визитов')

    const url = new URL(requests[0])

    expect(url.searchParams.get('from')).toBe('2026-10-10')
    expect(url.searchParams.get('to')).toBe('2026-11-08')
  })
})
