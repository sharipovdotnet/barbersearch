import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ReviewsScreen } from '@/components/admin/reviews-screen'
import { parseAdminPath } from '@/lib/admin-path'
import type { AdminReview } from '@/lib/admin-reviews'
import { useLocation } from '@/lib/router'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Экран отзывов: выбранная карточка не переезжает между вкладками.
 *
 * Сеть — только MSW на границе HTTP. Сценарий из баг-репорта: на широком экране
 * очередь сразу открывает первый отзыв, а смена вкладки не должна оставлять
 * в карточке строку прошлой вкладки. Русская копия и id отзывов — тестовые.
 */

const PENDING_TEXT = 'Текст отзыва на модерации'
const PUBLISHED_TEXT = 'Текст опубликованного отзыва'

const PENDING: AdminReview = {
  id: '9e800000-0000-4000-8000-000000000001',
  rating: 2,
  text: PENDING_TEXT,
  isPublished: false,
  createdAt: '2026-09-10T10:00:00+00:00',
  updatedAt: '2026-09-10T10:00:00+00:00',
  barber: {
    id: '9e800000-0000-4000-8000-000000000002',
    displayName: 'Артём Соколов',
    rating: 4.5,
    reviewsCount: 12,
    isActive: true,
    isVerified: true,
  },
  customer: {
    id: '9e800000-0000-4000-8000-000000000003',
    fullName: 'Дмитрий Крылов',
    email: 'dmitry@barbersearch.test',
  },
  visit: {
    bookingId: '9e800000-0000-4000-8000-000000000004',
    startAt: '2026-09-01T09:00:00+00:00',
    price: 2500,
    serviceName: 'Fade',
    salonName: 'Barbersearch на Ленина',
    timeZoneId: 'Asia/Yekaterinburg',
  },
}

const PUBLISHED: AdminReview = {
  ...PENDING,
  id: '9e800000-0000-4000-8000-000000000011',
  rating: 5,
  text: PUBLISHED_TEXT,
  isPublished: true,
  createdAt: '2026-09-09T10:00:00+00:00',
  customer: {
    id: '9e800000-0000-4000-8000-000000000013',
    fullName: 'Ирина Волкова',
    email: 'irina@barbersearch.test',
  },
}

const REVIEWS = [PENDING, PUBLISHED]

/** `matchMedia` отвечает «широкий экран»: очередь сразу открывает первую строку. */
function stubWideScreen(): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: true,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }))
}

/** Ответы списка по `status` и карточки по id; запросы записываются для отладки. */
function mockAdminReviews(): URLSearchParams[] {
  const requests: URLSearchParams[] = []

  server.use(
    http.get('/api/admin/reviews', ({ request }) => {
      const params = new URL(request.url).searchParams
      requests.push(params)

      return HttpResponse.json({
        items:
          params.get('status') === 'published' ? [PUBLISHED] : [PENDING],
        nextCursor: null,
      })
    }),
    http.get('/api/admin/reviews/:id', ({ params }) => {
      const review = REVIEWS.find((item) => item.id === params.id)

      return review
        ? HttpResponse.json(review)
        : HttpResponse.json(
            problemDetails(404, { errorCodes: ['review.notFound'] }),
            { status: 404 }
          )
    })
  )

  return requests
}

/** Повторяет разбор адреса из `AdminPage`: экран получает вкладку и id из URL. */
function AdminReviewsRoute() {
  const location = useLocation()
  const index = location.indexOf('?')
  const pathname = index === -1 ? location : location.slice(0, index)
  const search = index === -1 ? '' : location.slice(index)
  const route = parseAdminPath(pathname, search)

  return route.kind === 'reviews' ? (
    <ReviewsScreen tab={route.tab} reviewId={route.reviewId} />
  ) : null
}

function openAt(address: string) {
  window.history.replaceState(null, '', address)
  stubWideScreen()
}

describe('ReviewsScreen', () => {
  beforeEach(() => {
    mockAdminReviews()
  })

  it('при смене вкладки «На модерации» → «Опубликованные» открывает опубликованный отзыв', async () => {
    const user = userEvent.setup()

    openAt('/admin/reviews')
    render(
      <StrictMode>
        <AdminReviewsRoute />
      </StrictMode>
    )

    // Первая строка очереди модерации открыта автоматически на широком экране.
    expect(await screen.findByText(PENDING_TEXT)).toBeInTheDocument()
    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/reviews/${PENDING.id}`)
    })

    await user.click(screen.getByRole('tab', { name: 'Опубликованные' }))

    // «Чужой» отзыв из прошлой вкладки не должен остаться ни в адресе, ни в карточке.
    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/reviews/${PUBLISHED.id}`)
    })
    expect(await screen.findByText(PUBLISHED_TEXT)).toBeInTheDocument()
    expect(screen.queryByText(PENDING_TEXT)).not.toBeInTheDocument()
  })

  it('при смене вкладки «Опубликованные» → «На модерации» открывает отзыв на модерации', async () => {
    const user = userEvent.setup()

    openAt('/admin/reviews?tab=published')
    render(
      <StrictMode>
        <AdminReviewsRoute />
      </StrictMode>
    )

    expect(await screen.findByText(PUBLISHED_TEXT)).toBeInTheDocument()
    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/reviews/${PUBLISHED.id}`)
    })

    await user.click(screen.getByRole('tab', { name: 'На модерации' }))

    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/reviews/${PENDING.id}`)
    })
    expect(await screen.findByText(PENDING_TEXT)).toBeInTheDocument()
    expect(screen.queryByText(PUBLISHED_TEXT)).not.toBeInTheDocument()
  })
})
