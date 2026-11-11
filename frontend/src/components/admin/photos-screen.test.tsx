import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { PhotosScreen } from '@/components/admin/photos-screen'
import type {
  AdminAvatarPhoto,
  AdminPortfolioPhoto,
} from '@/lib/admin-photos'
import { parseAdminPath } from '@/lib/admin-path'
import { useLocation } from '@/lib/router'
import {
  noContent,
  problemDetails,
  unauthorized,
} from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Очередь премодерации фото: вкладки, поиск, решения и состояния.
 *
 * Сеть — только MSW на границе HTTP. Проверяем обещания экрана, которых не видит
 * unit-тест транспорта: настоящие ссылки вкладок, задержку поиска, удаление строки
 * после решения, `409` с перечитыванием очереди, пагинацию без запроса на «Назад»
 * и возврат фокуса из обоих диалогов. Русская копия сравнивается целиком.
 */

const AVATAR: AdminAvatarPhoto = {
  userId: '9e000000-0000-4000-8000-000000000101',
  firstName: 'Ирина',
  lastName: 'Волкова',
  email: 'irina@barbersearch.test',
  avatarUrl:
    '/api/media/avatars/9e000000-0000-4000-8000-000000000101/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg',
  moderationStatus: 'Pending',
  submittedAt: '2026-09-10T10:00:00+00:00',
}

const SECOND_AVATAR: AdminAvatarPhoto = {
  ...AVATAR,
  userId: '9e000000-0000-4000-8000-000000000102',
  firstName: 'Пётр',
  lastName: 'Зимин',
  email: 'petr@barbersearch.test',
  avatarUrl:
    '/api/media/avatars/9e000000-0000-4000-8000-000000000102/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg',
  submittedAt: '2026-09-11T10:00:00+00:00',
}

const PORTFOLIO: AdminPortfolioPhoto = {
  photoId: '9e100000-0000-4000-8000-000000000201',
  barberProfileId: '9e100000-0000-4000-8000-000000000202',
  barberDisplayName: 'Артём Соколов',
  ownerEmail: 'artem@barbersearch.test',
  url: '/api/media/portfolio/9e100000-0000-4000-8000-000000000202/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg',
  description: 'Fade с бородой',
  isPublished: true,
  moderationStatus: 'Pending',
  submittedAt: '2026-09-11T09:00:00+00:00',
}

/** Повторяет разбор адреса из `AdminPage`: экран получает вкладку из URL. */
function PhotosRoute() {
  const location = useLocation()
  const index = location.indexOf('?')
  const pathname = index === -1 ? location : location.slice(0, index)
  const search = index === -1 ? '' : location.slice(index)
  const route = parseAdminPath(pathname, search)

  return route.kind === 'photos' ? <PhotosScreen tab={route.tab} /> : null
}

/** Ответ очереди аватаров с запоминанием параметров каждого запроса. */
function mockAvatars(
  items: AdminAvatarPhoto[],
  nextCursor: string | null = null
): URLSearchParams[] {
  const requests: URLSearchParams[] = []

  server.use(
    http.get('/api/admin/photos/avatars', ({ request }) => {
      requests.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items, nextCursor })
    })
  )

  return requests
}

/** Ответ очереди портфолио: по умолчанию пустой, строки собирает тест. */
function mockPortfolio(
  items: AdminPortfolioPhoto[],
  nextCursor: string | null = null
): URLSearchParams[] {
  const requests: URLSearchParams[] = []

  server.use(
    http.get('/api/admin/photos/portfolio', ({ request }) => {
      requests.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items, nextCursor })
    })
  )

  return requests
}

beforeEach(() => {
  window.history.replaceState(null, '', '/admin/photos')
})

describe('PhotosScreen', () => {
  it('показывает очередь аватаров, вкладки-ссылки и строку с решениями', async () => {
    mockAvatars([AVATAR])
    mockPortfolio([PORTFOLIO])

    render(<PhotosRoute />)

    expect(
      screen.getByRole('heading', { name: 'Фотографии', level: 1 })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Очередь премодерации: новые аватары и работы не видны публично, пока вы их не одобрите. Откройте кадр крупно и примите решение.'
      )
    ).toBeInTheDocument()

    const tablist = screen.getByRole('tablist', {
      name: 'Какие фотографии показать',
    })

    expect(within(tablist).getByRole('tab', { name: 'Аватары' })).toHaveAttribute(
      'aria-selected',
      'true'
    )
    expect(
      within(tablist).getByRole('tab', { name: 'Портфолио' })
    ).toHaveAttribute('href', '/admin/photos?tab=portfolio')

    expect(await screen.findByText('Ирина Волкова')).toBeInTheDocument()
    expect(
      screen.getByText('irina@barbersearch.test')
    ).toBeInTheDocument()
    expect(screen.getByText(/ждёт с/)).toBeInTheDocument()

    const preview = screen.getByRole('button', {
      name: 'Открыть фото крупно: Ирина Волкова',
    })

    expect(preview.querySelector('img')).toHaveAttribute('src', AVATAR.avatarUrl)
    expect(
      screen.getByRole('button', { name: 'Одобрить: Ирина Волкова' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Отклонить: Ирина Волкова' })
    ).toBeInTheDocument()

    // Массовых действий и чекбоксов в этом срезе нет.
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(screen.queryByText('Одобрить все')).not.toBeInTheDocument()
  })

  it('переключает вкладку мышью и с клавиатуры, не теряя href', async () => {
    const user = userEvent.setup()

    mockAvatars([AVATAR])
    const portfolioRequests = mockPortfolio([PORTFOLIO])

    render(<PhotosRoute />)

    await screen.findByText('Ирина Волкова')

    const portfolioTab = screen.getByRole('tab', { name: 'Портфолио' })

    await user.click(portfolioTab)

    expect(await screen.findByText('Артём Соколов')).toBeInTheDocument()
    expect(screen.getByText('Fade с бородой')).toBeInTheDocument()
    expect(screen.getByText('artem@barbersearch.test')).toBeInTheDocument()
    expect(portfolioRequests.at(-1)?.has('search')).toBe(false)
    expect(portfolioTab).toHaveAttribute('aria-selected', 'true')
    expect(portfolioTab).toHaveAttribute('href', '/admin/photos?tab=portfolio')

    const avatarsTab = screen.getByRole('tab', { name: 'Аватары' })

    avatarsTab.focus()

    await user.keyboard('{ArrowLeft}')

    await waitFor(() => {
      expect(avatarsTab).toHaveAttribute('aria-selected', 'true')
    })
    expect(avatarsTab).toHaveFocus()
  })

  it('поиск отправляется с задержкой, сбрасывается и показывает пустой результат', async () => {
    const user = userEvent.setup()
    const requests = mockAvatars([])

    render(<PhotosRoute />)

    await screen.findByText('Очередь пуста — все фото проверены.')

    const searchbox = screen.getByRole('searchbox', {
      name: 'Поиск по имени или email',
    })

    expect(searchbox).toHaveAttribute('maxlength', '100')

    await user.type(searchbox, 'Ирина')

    await waitFor(
      () => {
        expect(requests.at(-1)?.get('search')).toBe('Ирина')
      },
      { timeout: 2000 }
    )

    expect(await screen.findByText('Ничего не найдено')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Очистить поиск' }))

    await waitFor(() => {
      expect(requests.at(-1)?.has('search')).toBe(false)
    })
    expect(
      await screen.findByText('Очередь пуста — все фото проверены.')
    ).toBeInTheDocument()
  })

  it('«Одобрить» убирает строку, объявляет успех и перечитывает очередь', async () => {
    const user = userEvent.setup()
    const approved: string[] = []
    let listCalls = 0

    server.use(
      http.get('/api/admin/photos/avatars', () => {
        listCalls += 1

        return HttpResponse.json({
          items: listCalls === 1 ? [AVATAR] : [],
          nextCursor: null,
        })
      }),
      http.post('/api/admin/users/:userId/avatar/approval', ({ params }) => {
        approved.push(String(params.userId))

        return noContent()
      })
    )

    render(<PhotosRoute />)

    const approve = await screen.findByRole('button', {
      name: 'Одобрить: Ирина Волкова',
    })

    await user.click(approve)

    await waitFor(() => {
      expect(approved).toEqual([AVATAR.userId])
    })
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото одобрено.'
    )
    await waitFor(() => {
      expect(screen.queryByText('Ирина Волкова')).not.toBeInTheDocument()
    })
    await waitFor(() => {
      expect(listCalls).toBeGreaterThanOrEqual(2)
    })
  })

  it('«Отклонить» требует причину, отправляет её и закрывает строку', async () => {
    const user = userEvent.setup()
    const reasons: unknown[] = []
    let listCalls = 0

    server.use(
      http.get('/api/admin/photos/avatars', () => {
        listCalls += 1

        return HttpResponse.json({
          items: listCalls === 1 ? [AVATAR] : [],
          nextCursor: null,
        })
      }),
      http.post(
        '/api/admin/users/:userId/avatar/rejection',
        async ({ request }) => {
          reasons.push(await request.json())

          return noContent()
        }
      )
    )

    render(<PhotosRoute />)

    await user.click(
      await screen.findByRole('button', {
        name: 'Отклонить: Ирина Волкова',
      })
    )

    const dialog = await screen.findByRole('alertdialog')

    expect(within(dialog).getByText('Отклонить фото?')).toBeInTheDocument()
    expect(within(dialog).getByText('Ирина Волкова')).toBeInTheDocument()

    const reason = within(dialog).getByLabelText('Причина отказа')

    expect(reason).toHaveAttribute('maxlength', '500')

    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Укажите причину отказа.'
    )
    expect(reasons).toHaveLength(0)

    await user.type(reason, '  Видно лицо не полностью  ')
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    await waitFor(() => {
      expect(reasons).toEqual([{ reason: 'Видно лицо не полностью' }])
    })
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото отклонено.'
    )
    await waitFor(() => {
      expect(screen.queryByText('Ирина Волкова')).not.toBeInTheDocument()
    })
  })

  it('отказ в диалоге причины возвращает фокус на кнопку строки', async () => {
    const user = userEvent.setup()

    mockAvatars([AVATAR])

    render(<PhotosRoute />)

    const reject = await screen.findByRole('button', {
      name: 'Отклонить: Ирина Волкова',
    })

    await user.click(reject)
    await screen.findByRole('alertdialog')
    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    })
    await waitFor(() => {
      expect(reject).toHaveFocus()
    })
  })

  it('409 говорит «уже проверено», убирает строку и обновляет очередь', async () => {
    const user = userEvent.setup()
    const requests: URLSearchParams[] = []
    let listCalls = 0

    server.use(
      http.get('/api/admin/photos/avatars', ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        listCalls += 1

        return HttpResponse.json({
          items: listCalls === 1 ? [AVATAR] : [],
          nextCursor: null,
        })
      }),
      http.post('/api/admin/users/:userId/avatar/approval', () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['user.avatarModeration.invalidStatus'],
          }),
          { status: 409 }
        )
      )
    )

    render(<PhotosRoute />)

    await user.click(
      await screen.findByRole('button', {
        name: 'Одобрить: Ирина Волкова',
      })
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Фото уже проверено. Обновите список.'
    )
    await waitFor(() => {
      expect(screen.queryByText('Ирина Волкова')).not.toBeInTheDocument()
    })
    await waitFor(() => {
      expect(requests.length).toBeGreaterThanOrEqual(2)
    })
  })

  it('превью открывается крупно, Escape закрывает и возвращает фокус', async () => {
    const user = userEvent.setup()

    mockAvatars([AVATAR])

    render(<PhotosRoute />)

    const preview = await screen.findByRole('button', {
      name: 'Открыть фото крупно: Ирина Волкова',
    })

    await user.click(preview)

    const dialog = await screen.findByRole('dialog')

    expect(within(dialog).getByRole('heading', { name: 'Ирина Волкова' })).toBeInTheDocument()
    expect(
      within(dialog).getByRole('img', { name: 'Фото: Ирина Волкова' })
    ).toHaveAttribute('src', AVATAR.avatarUrl)
    expect(
      within(dialog).queryByRole('button', { name: /обрезать|увеличить/i })
    ).not.toBeInTheDocument()

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
    await waitFor(() => {
      expect(preview).toHaveFocus()
    })
  })

  it('портфолио одобряется эндпоинтом фото с profileId и photoId', async () => {
    const user = userEvent.setup()
    const decisions: string[] = []

    window.history.replaceState(null, '', '/admin/photos?tab=portfolio')

    let listCalls = 0

    server.use(
      http.get('/api/admin/photos/portfolio', () => {
        listCalls += 1

        return HttpResponse.json({
          items: listCalls === 1 ? [PORTFOLIO] : [],
          nextCursor: null,
        })
      }),
      http.post(
        '/api/barber-profiles/:barberProfileId/photos/:photoId/approval',
        ({ params }) => {
          decisions.push(`${params.barberProfileId}/${params.photoId}`)

          return noContent()
        }
      )
    )

    render(<PhotosRoute />)

    expect(await screen.findByText('Артём Соколов')).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', {
        name: 'Одобрить: Артём Соколов: Fade с бородой',
      })
    )

    await waitFor(() => {
      expect(decisions).toEqual([
        `${PORTFOLIO.barberProfileId}/${PORTFOLIO.photoId}`,
      ])
    })
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото одобрено.'
    )
  })

  it('пустая очередь называет себя честно', async () => {
    mockAvatars([])

    render(<PhotosRoute />)

    expect(
      await screen.findByText('Очередь пуста — все фото проверены.')
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Новые аватары и работы появятся здесь, как только их загрузят.'
      )
    ).toBeInTheDocument()
  })

  it('401 показывает гейт входа и не рисует очередь', async () => {
    server.use(http.get('/api/admin/photos/avatars', () => unauthorized()))

    render(<PhotosRoute />)

    expect(
      await screen.findByText('Вход для администраторов')
    ).toBeInTheDocument()
    expect(
      screen.queryByText('Очередь пуста — все фото проверены.')
    ).not.toBeInTheDocument()
  })

  it('403 показывает текст про роль, а не английский detail', async () => {
    server.use(
      http.get('/api/admin/photos/avatars', () =>
        HttpResponse.json(
          problemDetails(403, {
            detail: 'Forbidden.',
            errorCodes: ['auth.forbidden'],
          }),
          { status: 403 }
        )
      )
    )

    render(<PhotosRoute />)

    expect(
      await screen.findByText('Раздел доступен только администратору.')
    ).toBeInTheDocument()
    expect(
      screen.getByText('Не удалось загрузить фотографии')
    ).toBeInTheDocument()
    expect(screen.queryByText('Forbidden.')).not.toBeInTheDocument()
  })

  it('первая ошибка повторяется кнопкой «Повторить»', async () => {
    const user = userEvent.setup()
    let calls = 0

    server.use(
      http.get('/api/admin/photos/avatars', () => {
        calls += 1

        return calls === 1
          ? HttpResponse.json(problemDetails(500), { status: 500 })
          : HttpResponse.json({ items: [AVATAR], nextCursor: null })
      })
    )

    render(<PhotosRoute />)

    expect(
      await screen.findByText('Не удалось загрузить фотографии')
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(await screen.findByText('Ирина Волкова')).toBeInTheDocument()
    expect(calls).toBe(2)
  })

  it('ошибка следующей страницы не стирает строки', async () => {
    const user = userEvent.setup()

    server.use(
      http.get('/api/admin/photos/avatars', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')

        if (cursor === null) {
          return HttpResponse.json({
            items: [AVATAR],
            nextCursor: 'cursor-2',
          })
        }

        return HttpResponse.json(
          problemDetails(500, { detail: 'Boom.', errorCodes: ['server.error'] }),
          { status: 500 }
        )
      })
    )

    render(<PhotosRoute />)

    expect(await screen.findByText('Ирина Волкова')).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', {
        name: 'Дальше — следующая страница',
      })
    )

    expect(
      await screen.findByText(
        'Не удалось загрузить следующую страницу фотографий.'
      )
    ).toBeInTheDocument()
    expect(screen.getByText('Ирина Волкова')).toBeInTheDocument()
    expect(
      screen.getByRole('button', {
        name: 'Повторить — загрузить следующую страницу',
      })
    ).toBeInTheDocument()
  })

  it('«Назад» возвращает на первую страницу без запроса', async () => {
    const user = userEvent.setup()
    const cursors: (string | null)[] = []

    server.use(
      http.get('/api/admin/photos/avatars', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')

        cursors.push(cursor)

        return cursor === null
          ? HttpResponse.json({
              items: [AVATAR],
              nextCursor: 'cursor-2',
            })
          : HttpResponse.json({
              items: [SECOND_AVATAR],
              nextCursor: null,
            })
      })
    )

    render(<PhotosRoute />)

    expect(await screen.findByText('Ирина Волкова')).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', {
        name: 'Дальше — следующая страница',
      })
    )

    expect(await screen.findByText('Пётр Зимин')).toBeInTheDocument()
    expect(cursors).toEqual([null, 'cursor-2'])

    await user.click(
      screen.getByRole('button', {
        name: 'Назад — предыдущая страница',
      })
    )

    expect(await screen.findByText('Ирина Волкова')).toBeInTheDocument()
    expect(cursors).toEqual([null, 'cursor-2'])
  })
})
