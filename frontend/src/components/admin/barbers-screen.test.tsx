import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { BarbersScreen } from '@/components/admin/barbers-screen'
import type { AdminBarber, AdminBarberDetail } from '@/lib/admin-barbers'
import { parseAdminPath } from '@/lib/admin-path'
import { useLocation } from '@/lib/router'
import { server } from '@/test/msw/server'

/**
 * Экран барберов: выбранная карточка принадлежит текущему фильтру.
 *
 * Сеть — только MSW на границе HTTP. Сценарий из баг-репорта: на широком экране
 * очередь сразу открывает первую строку, а смена фильтра не должна оставлять
 * в карточке профиль прошлой выборки. Русская копия и id профилей — тестовые.
 */

const VERIFIED: AdminBarber = {
  id: '9e800000-0000-4000-8000-000000000001',
  displayName: 'Артём Соколов',
  email: 'artem@barbersearch.test',
  grade: 'ArtDirector',
  rating: 4.5,
  reviewsCount: 29,
  isActive: true,
  isVerified: true,
  createdAt: '2026-09-28T15:12:00+00:00',
  updatedAt: '2026-11-02T10:53:00+00:00',
}

const PENDING: AdminBarber = {
  ...VERIFIED,
  id: '9e800000-0000-4000-8000-000000000002',
  displayName: 'Иван Петров',
  email: 'ivan@barbersearch.test',
  grade: 'MiddleBarber',
  rating: null,
  reviewsCount: 0,
  isVerified: false,
}

/** В фикстурах нет скрытых профилей: очередь «Скрытые» пуста, как на скриншоте. */
const ROWS = [VERIFIED, PENDING]

function toDetail(row: AdminBarber): AdminBarberDetail {
  return {
    ...row,
    userId: `9e800000-0000-4000-8000-00000000000${row.id.at(-1)}`,
    phoneNumber: null,
    avatarUrl: null,
    bio: null,
    yearsOfExperience: 5,
    readiness: {
      isProfileFilled: false,
      isEmailVerified: true,
      servicesCount: 0,
      salonsCount: 0,
      scheduleDaysCount: 0,
      photosCount: 0,
    },
  }
}

/** `matchMedia` отвечает «широкий экран»: список сразу открывает первую строку. */
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

/** Ответы списка по `status`/`search` и карточки по id; запросы пишутся для отладки. */
function mockAdminBarbers(): URLSearchParams[] {
  const requests: URLSearchParams[] = []

  server.use(
    http.get('/api/admin/barbers', ({ request }) => {
      const params = new URL(request.url).searchParams
      const status = params.get('status')
      const search = (params.get('search') ?? '').toLowerCase()

      requests.push(params)

      const matching = ROWS.filter((row) => {
        if (status === 'pending' && row.isVerified) {
          return false
        }

        if (status === 'hidden' && row.isActive) {
          return false
        }

        return (
          search === '' ||
          `${row.displayName} ${row.email}`.toLowerCase().includes(search)
        )
      })

      return HttpResponse.json({ items: matching, nextCursor: null })
    }),
    http.get('/api/admin/barbers/:id', ({ params }) => {
      const row = ROWS.find((item) => item.id === params.id)

      return row ? HttpResponse.json(toDetail(row)) : new HttpResponse(null, { status: 404 })
    })
  )

  return requests
}

/** Повторяет разбор адреса из `AdminPage`: экран получает фильтр и id из URL. */
function AdminBarbersRoute() {
  const location = useLocation()
  const index = location.indexOf('?')
  const pathname = index === -1 ? location : location.slice(0, index)
  const search = index === -1 ? '' : location.slice(index)
  const route = parseAdminPath(pathname, search)

  return route.kind === 'barbers' ? (
    <BarbersScreen barberId={route.barberId} status={route.status} />
  ) : null
}

function openAt(address: string) {
  window.history.replaceState(null, '', address)
  stubWideScreen()
}

describe('BarbersScreen', () => {
  beforeEach(() => {
    mockAdminBarbers()
  })

  it('смена фильтра не оставляет карточку барбера из прошлой выборки', async () => {
    const user = userEvent.setup()

    openAt('/admin/barbers?status=all')
    render(
      <StrictMode>
        <AdminBarbersRoute />
      </StrictMode>
    )

    // На широком экране первая строка фильтра «Все» открыта автоматически.
    expect(
      await screen.findByRole('region', { name: VERIFIED.displayName })
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/barbers/${VERIFIED.id}`)
    })

    await user.click(screen.getByRole('button', { name: 'Скрытые' }))

    // «Скрытых» в фикстурах нет: карточка прошлого фильтра не должна остаться.
    expect(await screen.findByText('Скрытых профилей нет')).toBeInTheDocument()
    await waitFor(() => {
      expect(window.location.pathname).toBe('/admin/barbers')
    })
    expect(window.location.search).toBe('?status=hidden')
    expect(
      screen.queryByRole('region', { name: VERIFIED.displayName })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByText('В загруженной странице нет барберов.')
    ).not.toBeInTheDocument()
  })

  it('смена фильтра открывает первую строку нового фильтра', async () => {
    const user = userEvent.setup()

    openAt('/admin/barbers')
    render(
      <StrictMode>
        <AdminBarbersRoute />
      </StrictMode>
    )

    expect(
      await screen.findByRole('region', { name: PENDING.displayName })
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Все' }))

    expect(
      await screen.findByRole('region', { name: VERIFIED.displayName })
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/barbers/${VERIFIED.id}`)
    })
    expect(window.location.search).toBe('?status=all')
  })

  it('применённый поиск не оставляет карточку из прошлой выборки', async () => {
    const user = userEvent.setup()

    openAt('/admin/barbers?status=all')
    render(
      <StrictMode>
        <AdminBarbersRoute />
      </StrictMode>
    )

    expect(
      await screen.findByRole('region', { name: VERIFIED.displayName })
    ).toBeInTheDocument()

    await user.type(
      screen.getByRole('searchbox', { name: 'Поиск по имени или email' }),
      'Иван'
    )

    expect(
      await screen.findByRole(
        'region',
        { name: PENDING.displayName },
        { timeout: 2000 }
      )
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(window.location.pathname).toBe(`/admin/barbers/${PENDING.id}`)
    })
    expect(window.location.search).toBe('?status=all')
    expect(
      screen.queryByRole('region', { name: VERIFIED.displayName })
    ).not.toBeInTheDocument()
  })
})
