import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { BarberRequestsScreen } from '@/components/admin/barber-requests-screen'
import type { AdminBarberRequest } from '@/lib/admin-barber-requests'
import { parseAdminPath } from '@/lib/admin-path'
import { useLocation } from '@/lib/router'
import { problemDetails, unauthorized } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Экран `/admin/barber-requests`: очередь, фильтр в адресе, split-view карточка
 * и два решения.
 *
 * Сеть — только MSW на границе HTTP, данные мутируются обработчиками `POST`, как
 * на сервере: после одобрения строка очереди действительно исчезает, а карточка
 * перечитывается. Русская копия сравнивается целиком: она может меняться.
 */

const LIST_URL = '/api/admin/barber-requests'

const PENDING: AdminBarberRequest = {
  id: '9ea00000-0000-4000-8000-000000000001',
  status: 'Pending',
  displayName: 'Иван Петров',
  yearsOfExperience: 7,
  city: 'Москва',
  about: 'Стригу и брею',
  hasSalon: false,
  createdAt: '2026-11-06T10:00:00+00:00',
  reviewedAt: null,
  rejectionReason: null,
  reviewedByUserId: null,
  applicant: {
    id: '9e000000-0000-4000-8000-000000000099',
    fullName: 'Иван Петров',
    email: 'e2e-applicant@barbersearch.test',
  },
}

const APPROVED: AdminBarberRequest = {
  ...PENDING,
  id: '9ea00000-0000-4000-8000-000000000002',
  status: 'Approved',
  displayName: 'Пётр Сидоров',
  city: 'Казань',
  // Позже очереди: в фильтре «Все» список идёт новыми сверху, и первая строка — эта.
  createdAt: '2026-11-07T10:00:00+00:00',
  reviewedAt: '2026-11-07T12:00:00+00:00',
  reviewedByUserId: '9e000000-0000-4000-8000-000000000001',
  applicant: {
    id: '9e000000-0000-4000-8000-000000000098',
    fullName: 'Пётр Сидоров',
    email: 'e2e-applicant-2@barbersearch.test',
  },
}

type ActionCall = {
  kind: 'approval' | 'rejection'
  body: string
}

type MockState = {
  listRequests: URLSearchParams[]
  actions: ActionCall[]
  rows: AdminBarberRequest[]
}

/** Мутируемое «серверное» состояние: решения меняют строки между запросами. */
function mockAdminRequests(initial: AdminBarberRequest[]): MockState {
  const state: MockState = {
    listRequests: [],
    actions: [],
    rows: initial.map((row) => ({ ...row })),
  }

  server.use(
    http.get(LIST_URL, ({ request }) => {
      const params = new URL(request.url).searchParams
      state.listRequests.push(params)

      const status = params.get('status')

      return HttpResponse.json({
        items:
          status === 'all'
            ? [...state.rows].sort((left, right) =>
                right.createdAt.localeCompare(left.createdAt)
              )
            : state.rows.filter((row) => row.status === 'Pending'),
        nextCursor: null,
      })
    }),

    http.get(`${LIST_URL}/:id`, ({ params }) => {
      const row = state.rows.find((item) => item.id === params.id)

      return row
        ? HttpResponse.json(row)
        : HttpResponse.json(
            problemDetails(404, { errorCodes: ['barberRequest.notFound'] }),
            { status: 404 }
          )
    }),

    http.post(`${LIST_URL}/:id/approval`, ({ params }) => {
      state.actions.push({ kind: 'approval', body: '' })

      const row = state.rows.find((item) => item.id === params.id)

      if (!row) {
        return new HttpResponse(null, { status: 404 })
      }

      const updated: AdminBarberRequest = {
        ...row,
        status: 'Approved',
        reviewedAt: '2026-11-06T12:00:00+00:00',
        reviewedByUserId: '9e000000-0000-4000-8000-000000000001',
      }

      state.rows = state.rows.map((item) =>
        item.id === updated.id ? updated : item
      )

      return HttpResponse.json(updated)
    }),

    http.post(`${LIST_URL}/:id/rejection`, async ({ request, params }) => {
      const body = await request.text()
      state.actions.push({ kind: 'rejection', body })

      const row = state.rows.find((item) => item.id === params.id)

      if (!row) {
        return new HttpResponse(null, { status: 404 })
      }

      const reason = (JSON.parse(body) as { reason: string }).reason

      const updated: AdminBarberRequest = {
        ...row,
        status: 'Rejected',
        reviewedAt: '2026-11-06T12:00:00+00:00',
        rejectionReason: reason,
      }

      state.rows = state.rows.map((item) =>
        item.id === updated.id ? updated : item
      )

      return HttpResponse.json(updated)
    })
  )

  return state
}

/** Повторяет разбор адреса из `AdminPage`: экран получает фильтр и id из URL. */
function AdminBarberRequestsRoute() {
  const location = useLocation()
  const index = location.indexOf('?')
  const pathname = index === -1 ? location : location.slice(0, index)
  const search = index === -1 ? '' : location.slice(index)
  const route = parseAdminPath(pathname, search)

  return route.kind === 'barberRequests' ? (
    <BarberRequestsScreen
      requestId={route.requestId}
      status={route.status}
    />
  ) : null
}

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

function openAt(address: string) {
  window.history.replaceState(null, '', address)
  stubWideScreen()
}

function renderScreen() {
  return render(<AdminBarberRequestsRoute />)
}

describe('BarberRequestsScreen', () => {
  beforeEach(() => {
    vi.unstubAllGlobals()
  })

  it('показывает очередь, открывает первую заявку и не шлёт курсор первой страницы', async () => {
    const state = mockAdminRequests([PENDING])

    openAt('/admin/barber-requests')
    renderScreen()

    expect(
      await screen.findByRole('region', { name: PENDING.displayName })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', { name: 'Заявки', level: 1 })
    ).toBeInTheDocument()

    await waitFor(() => {
      expect(window.location.pathname).toBe(
        `/admin/barber-requests/${PENDING.id}`
      )
    })

    const params = state.listRequests[0]

    expect(params.get('status')).toBe('pending')
    expect(params.has('cursor')).toBe(false)
  })

  it('смена фильтра начинает новую выборку и не оставляет карточку прошлой', async () => {
    const user = userEvent.setup()
    const state = mockAdminRequests([PENDING, APPROVED])

    openAt('/admin/barber-requests')
    renderScreen()

    expect(
      await screen.findByRole('region', { name: PENDING.displayName })
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Все' }))

    expect(
      await screen.findByRole('region', { name: APPROVED.displayName })
    ).toBeInTheDocument()

    await waitFor(() => {
      expect(window.location.pathname).toBe(
        `/admin/barber-requests/${APPROVED.id}`
      )
    })
    expect(window.location.search).toBe('?status=all')
    expect(state.listRequests.at(-1)?.get('status')).toBe('all')
    expect(state.listRequests.at(-1)?.has('cursor')).toBe(false)
  })

  it('одобрение: alertdialog предупреждает о сессиях и строка уходит из очереди', async () => {
    const user = userEvent.setup()
    const state = mockAdminRequests([PENDING])

    openAt('/admin/barber-requests')
    renderScreen()

    const detail = await screen.findByRole('region', {
      name: PENDING.displayName,
    })

    await user.click(within(detail).getByRole('button', { name: 'Одобрить' }))

    const dialog = screen.getByRole('alertdialog')

    expect(dialog).toHaveTextContent('Одобрить заявку?')
    expect(dialog).toHaveTextContent(/завершит все его сессии/)
    expect(dialog).toHaveTextContent(/создаст профиль барбера/)

    await user.click(within(dialog).getByRole('button', { name: 'Одобрить' }))

    expect(await screen.findByRole('status')).toHaveTextContent(/одобрена/)

    await waitFor(() => {
      expect(state.actions).toStrictEqual([{ kind: 'approval', body: '' }])
    })

    // Очередь перечитана: одобренная заявка в неё больше не входит.
    expect(
      await screen.findByText('В загруженной странице нет заявок.')
    ).toBeInTheDocument()

    await waitFor(() => {
      expect(
        within(
          screen.getByRole('region', { name: PENDING.displayName })
        ).getByText('Одобрена')
      ).toBeInTheDocument()
    })
  })

  it('отказ требует причину: пустая не отправляется, заполненная уходит в теле', async () => {
    const user = userEvent.setup()
    const state = mockAdminRequests([PENDING])

    openAt('/admin/barber-requests')
    renderScreen()

    const detail = await screen.findByRole('region', {
      name: PENDING.displayName,
    })

    await user.click(within(detail).getByRole('button', { name: 'Отклонить' }))

    const dialog = screen.getByRole('alertdialog')

    expect(dialog).toHaveTextContent('Отклонить заявку?')

    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(
      within(dialog).getByText('Укажите причину отказа.')
    ).toBeInTheDocument()
    expect(state.actions).toStrictEqual([])

    await user.type(
      within(dialog).getByLabelText('Причина отказа'),
      'Не хватает подтверждённого опыта'
    )
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(await screen.findByRole('status')).toHaveTextContent(/отклонена/)

    await waitFor(() => {
      expect(state.actions).toStrictEqual([
        {
          kind: 'rejection',
          body: JSON.stringify({ reason: 'Не хватает подтверждённого опыта' }),
        },
      ])
    })
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('403 показывает текст про роль, а не английский detail', async () => {
    server.use(
      http.get(LIST_URL, () =>
        HttpResponse.json(
          problemDetails(403, {
            detail: 'Forbidden.',
            errorCodes: ['auth.forbidden'],
          }),
          { status: 403 }
        )
      )
    )

    openAt('/admin/barber-requests')
    renderScreen()

    expect(
      await screen.findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()
    expect(screen.getByText('Не удалось загрузить заявки')).toBeInTheDocument()
    expect(screen.queryByText('Forbidden.')).not.toBeInTheDocument()
  })

  it('401 показывает гейт входа', async () => {
    server.use(http.get(LIST_URL, () => unauthorized()))

    openAt('/admin/barber-requests')
    renderScreen()

    expect(
      await screen.findByText('Вход для администраторов')
    ).toBeInTheDocument()
  })

  it('404 на решение убирает строку и объявляет это через role="status"', async () => {
    const user = userEvent.setup()
    let removed = false

    server.use(
      http.get(LIST_URL, () =>
        HttpResponse.json({ items: removed ? [] : [PENDING], nextCursor: null })
      ),
      http.get(`${LIST_URL}/:id`, () =>
        removed
          ? HttpResponse.json(
              problemDetails(404, { errorCodes: ['barberRequest.notFound'] }),
              { status: 404 }
            )
          : HttpResponse.json(PENDING)
      ),
      http.post(`${LIST_URL}/:id/approval`, () => {
        removed = true

        return HttpResponse.json(
          problemDetails(404, { errorCodes: ['barberRequest.notFound'] }),
          { status: 404 }
        )
      })
    )

    openAt('/admin/barber-requests')
    renderScreen()

    const detail = await screen.findByRole('region', {
      name: PENDING.displayName,
    })

    await user.click(within(detail).getByRole('button', { name: 'Одобрить' }))
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Одобрить',
      })
    )

    const announcement = await screen.findByText(/больше не существует/)

    expect(announcement.closest('[role="status"]')).not.toBeNull()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(
      await screen.findByText('В загруженной странице нет заявок.')
    ).toBeInTheDocument()
  })

  it('пустая очередь и пустой список всех заявок — разные тексты', async () => {
    mockAdminRequests([])

    openAt('/admin/barber-requests')
    const { unmount } = renderScreen()

    expect(await screen.findByText('Заявок нет')).toBeInTheDocument()

    unmount()
    window.history.replaceState(null, '', '/admin/barber-requests?status=all')

    renderScreen()

    expect(
      await screen.findByText('Заявок с таким статусом нет')
    ).toBeInTheDocument()
  })

  it('«Дальше» грузит следующую страницу по курсору, «Назад» запросов не делает', async () => {
    const user = userEvent.setup()
    const cursors: (string | null)[] = []

    server.use(
      http.get(LIST_URL, ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')
        cursors.push(cursor)

        return cursor === null
          ? HttpResponse.json({ items: [PENDING], nextCursor: 'cursor-2' })
          : HttpResponse.json({ items: [APPROVED], nextCursor: null })
      }),
      http.get(`${LIST_URL}/:id`, ({ params }) => {
        const row = [PENDING, APPROVED].find((item) => item.id === params.id)

        return row ? HttpResponse.json(row) : new HttpResponse(null, { status: 404 })
      })
    )

    openAt('/admin/barber-requests')
    renderScreen()

    await screen.findByRole('region', { name: PENDING.displayName })

    await user.click(
      screen.getByRole('button', { name: 'Дальше — следующая страница' })
    )

    expect(
      await screen.findByRole('link', { name: /Пётр Сидоров/ })
    ).toBeInTheDocument()
    expect(cursors.at(-1)).toBe('cursor-2')

    const requestsBefore = countRequests(LIST_URL)

    await user.click(
      screen.getByRole('button', { name: 'Назад — предыдущая страница' })
    )

    expect(await screen.findByRole('link', { name: /Иван Петров/ })).toBeInTheDocument()
    // Кэш страниц: «Назад» не ходит в сеть.
    expect(requestsBefore()).toBe(0)
  })
})
