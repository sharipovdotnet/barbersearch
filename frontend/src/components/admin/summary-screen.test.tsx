import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse, type JsonBodyType } from 'msw'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { SummaryScreen } from '@/components/admin/summary-screen'
import { AUTO_REFRESH_INTERVAL_MS } from '@/hooks/use-auto-refresh'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Экран сводки: три блока, честные нули, гейт входа и ошибки.
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания экрана, которых не видит
 * unit-тест транспорта: числа и ссылки из контракта, тексты пустых очередей, `401`
 * без запроса блоков, первую ошибку с «Повторить» и ошибку обновления, которая
 * не стирает блоки. Русская копия сравнивается целиком.
 */

const URL = '/api/admin/summary'

const OLDEST = {
  id: '9e800000-0000-4000-8000-000000000001',
  rating: 4,
  createdAt: '2026-09-10T10:00:00+00:00',
  barberDisplayName: 'Артём Соколов',
  customerDisplayName: 'Дмитрий Крылов',
}

const SUMMARY = {
  reviews: { pendingCount: 14, oldest: OLDEST },
  barbers: { unverifiedCount: 1 },
  salons: { inactiveCount: 1 },
}

const EMPTY = {
  reviews: { pendingCount: 0, oldest: null },
  barbers: { unverifiedCount: 0 },
  salons: { inactiveCount: 0 },
}

/** Ответ сводки с запоминанием каждого запроса. */
function mockSummary(body: JsonBodyType): string[] {
  const requests: string[] = []

  server.use(
    http.get(URL, ({ request }) => {
      requests.push(request.url)

      return HttpResponse.json(body)
    })
  )

  return requests
}

describe('SummaryScreen', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('показывает три блока с числами и ссылками из контракта', async () => {
    mockSummary(SUMMARY)

    render(<SummaryScreen />)

    expect(
      screen.getByRole('heading', { name: 'Сводка', level: 1 })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Что требует внимания. Счётчики считает сервер, разделы открываются ссылками.'
      )
    ).toBeInTheDocument()

    const reviews = await screen.findByRole('region', {
      name: 'Отзывы на модерации',
    })

    expect(within(reviews).getByText('14 отзывов')).toBeInTheDocument()
    expect(within(reviews).getByText(/Самый старый/)).toBeInTheDocument()
    expect(within(reviews).getByText('Оценка: 4 из 5')).toBeInTheDocument()
    expect(within(reviews).getByText('Клиент: Дмитрий Крылов')).toBeInTheDocument()
    expect(within(reviews).getByText('Барбер: Артём Соколов')).toBeInTheDocument()
    expect(
      within(reviews).getByRole('link', { name: 'Открыть отзыв' })
    ).toHaveAttribute('href', `/admin/reviews/${OLDEST.id}`)
    expect(
      within(reviews).getByRole('link', { name: 'К очереди отзывов' })
    ).toHaveAttribute('href', '/admin/reviews')

    const barbers = screen.getByRole('region', {
      name: 'Барберы без подтверждения',
    })

    expect(within(barbers).getByText('1 профиль')).toBeInTheDocument()
    expect(
      within(barbers).getByRole('link', { name: 'Открыть барберов' })
    ).toHaveAttribute('href', '/admin/barbers')

    const salons = screen.getByRole('region', { name: 'Выключенные салоны' })

    expect(within(salons).getByText('1 салон')).toBeInTheDocument()
    expect(
      within(salons).getByRole('link', { name: 'Открыть каталог салонов' })
    ).toHaveAttribute('href', '/admin/catalogue?tab=salons')
  })

  it('нули называет честно и не показывает «самого старого»', async () => {
    mockSummary(EMPTY)

    render(<SummaryScreen />)

    expect(
      await screen.findByText('Очередь пуста — все отзывы проверены.')
    ).toBeInTheDocument()
    expect(screen.getByText('Все профили подтверждены.')).toBeInTheDocument()
    expect(screen.getByText('Выключенных салонов нет.')).toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Открыть отзыв' })
    ).not.toBeInTheDocument()
  })

  it('401 показывает гейт входа и не рисует блоки', async () => {
    server.use(
      http.get(URL, () =>
        HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.required'] }),
          { status: 401 }
        )
      )
    )

    render(<SummaryScreen />)

    expect(
      await screen.findByText('Вход для администраторов')
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Отзывы на модерации' })
    ).not.toBeInTheDocument()
  })

  it('первая ошибка показывает карточку с «Повторить», повтор читает снова', async () => {
    const user = userEvent.setup()
    let calls = 0

    server.use(
      http.get(URL, () => {
        calls += 1

        return calls === 1
          ? HttpResponse.json(problemDetails(403), { status: 403 })
          : HttpResponse.json(SUMMARY)
      })
    )

    render(<SummaryScreen />)

    expect(
      await screen.findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()
    expect(screen.getByText('Не удалось загрузить сводку')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(
      await screen.findByRole('region', { name: 'Отзывы на модерации' })
    ).toBeInTheDocument()
    expect(calls).toBe(2)
  })

  it('ошибка обновления остаётся строкой и не стирает блоки', async () => {
    const user = userEvent.setup()
    let calls = 0

    server.use(
      http.get(URL, () => {
        calls += 1

        return calls === 1
          ? HttpResponse.json(SUMMARY)
          : HttpResponse.json(problemDetails(500), { status: 500 })
      })
    )

    render(<SummaryScreen />)

    const reviews = await screen.findByRole('region', {
      name: 'Отзывы на модерации',
    })

    await user.click(screen.getByRole('button', { name: 'Обновить' }))

    const alert = await screen.findByRole('alert')

    expect(alert).toHaveTextContent('Не удалось загрузить сводку.')
    expect(
      screen.getByRole('region', { name: 'Отзывы на модерации' })
    ).toBeInTheDocument()
    expect(within(reviews).getByText('14 отзывов')).toBeInTheDocument()
    expect(calls).toBe(2)
  })

  it('«Обновить» делает ровно один запрос и не мигает скелетоном', async () => {
    const user = userEvent.setup()
    const requests = mockSummary(SUMMARY)

    render(<SummaryScreen />)

    await screen.findByRole('region', { name: 'Отзывы на модерации' })

    expect(requests).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Обновить' }))

    await waitFor(() => {
      expect(requests).toHaveLength(2)
    })

    expect(screen.queryByText('Загружаем сводку…')).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Обновляем…' })
    ).not.toBeInTheDocument()
    expect(screen.getByText(/Обновлено в \d{2}:\d{2}/)).toBeInTheDocument()
  })

  it('через период опроса сама перечитывает сводку', async () => {
    vi.useFakeTimers()
    const requests = mockSummary(SUMMARY)

    render(<SummaryScreen />)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(requests).toHaveLength(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(AUTO_REFRESH_INTERVAL_MS - 1)
    })
    expect(requests).toHaveLength(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(1)
    })
    expect(requests).toHaveLength(2)
  })
})
