import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import {
  ADMIN_PHOTOS_PAGE_SIZE,
  ADMIN_PHOTO_SEARCH_MAX_LENGTH,
  approveAvatar,
  approvePortfolioPhoto,
  describeAdminPhotoListError,
  describePhotoModerationError,
  fetchAdminAvatarPhotos,
  fetchAdminPortfolioPhotos,
  isAdminPhotoUnauthorized,
  rejectAvatar,
  rejectPortfolioPhoto,
  type AdminAvatarPhoto,
  type AdminPortfolioPhoto,
} from '@/lib/admin-photos'
import { ApiError } from '@/lib/api'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Транспорт очереди премодерации фото.
 *
 * Оба чтения — `Pending`-очередь с `pageSize=20`, поиском и непрозрачным курсором;
 * лишние поля ответа guard игнорирует, а пропущенный статус — битый контракт.
 * Четыре решения — cookie-мутации с `X-CSRF-TOKEN`; повторное решение приходит
 * `409` и лечится обновлением списка, а причину отказа пользователь вводит сам.
 */

const AVATARS_URL = '/api/admin/photos/avatars'
const PORTFOLIO_URL = '/api/admin/photos/portfolio'

const USER_ID = '9e000000-0000-4000-8000-000000000003'
const BARBER_ID = '9e100000-0000-4000-8000-000000000015'
const PHOTO_ID = '9e200000-0000-4000-8000-000000000001'

const AVATAR_ROW: AdminAvatarPhoto = {
  userId: USER_ID,
  firstName: 'Ева',
  lastName: 'Клиентова',
  email: 'eva@barbersearch.test',
  avatarUrl: `/api/media/avatars/${USER_ID}/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg`,
  moderationStatus: 'Pending',
  submittedAt: '2026-11-11T10:00:00+00:00',
}

const PORTFOLIO_ROW: AdminPortfolioPhoto = {
  photoId: PHOTO_ID,
  barberProfileId: BARBER_ID,
  barberDisplayName: 'Артём Соколов',
  ownerEmail: 'artem@barbersearch.test',
  url: `/api/media/portfolio/${BARBER_ID}/9e200000000040008000000000000001.jpg`,
  description: 'Фейд',
  isPublished: true,
  moderationStatus: 'Pending',
  submittedAt: '2026-11-11T10:00:00+00:00',
}

function captureList(path: string): URLSearchParams[] {
  const captured: URLSearchParams[] = []

  server.use(
    http.get(path, ({ request }) => {
      captured.push(new URL(request.url).searchParams)

      return HttpResponse.json({ items: [], nextCursor: null })
    })
  )

  return captured
}

function entries(params: URLSearchParams): [string, string][] {
  return [...params.entries()]
}

function apiError(status: number, code: string): ApiError {
  return new ApiError(
    status,
    { status, title: 'Ошибка', errorCodes: [code] },
    'Request failed'
  )
}

describe('fetchAdminAvatarPhotos', () => {
  it('шлёт pageSize, поиск и курсор в одном запросе', async () => {
    const captured = captureList(AVATARS_URL)

    await fetchAdminAvatarPhotos({
      search: 'ева',
      cursor: 'cursor-1',
    })

    expect(captured).toHaveLength(1)
    expect(ADMIN_PHOTO_SEARCH_MAX_LENGTH).toBe(100)
    expect(entries(captured[0])).toStrictEqual([
      ['pageSize', String(ADMIN_PHOTOS_PAGE_SIZE)],
      ['search', 'ева'],
      ['cursor', 'cursor-1'],
    ])
  })

  it('не отправляет пустой поиск и первую страницу без курсора', async () => {
    const captured = captureList(AVATARS_URL)

    await fetchAdminAvatarPhotos({ search: '', cursor: null })

    expect(entries(captured[0])).toStrictEqual([
      ['pageSize', String(ADMIN_PHOTOS_PAGE_SIZE)],
    ])
  })

  it('возвращает разобранную страницу очереди', async () => {
    server.use(
      http.get(AVATARS_URL, () =>
        HttpResponse.json({
          items: [AVATAR_ROW],
          nextCursor: 'next-1',
        })
      )
    )

    await expect(fetchAdminAvatarPhotos({})).resolves.toStrictEqual({
      items: [AVATAR_ROW],
      nextCursor: 'next-1',
    })
  })

  it('битый ответ страницы — ошибка экрана, а не пустая очередь', async () => {
    for (const body of [
      { items: [{ ...AVATAR_ROW, moderationStatus: undefined }], nextCursor: null },
      { items: [{ ...AVATAR_ROW, avatarUrl: null }], nextCursor: null },
      { items: [], nextCursor: 42 },
      { items: [null], nextCursor: null },
    ]) {
      server.use(http.get(AVATARS_URL, () => HttpResponse.json(body)))

      await expect(fetchAdminAvatarPhotos({})).rejects.toThrow(
        'Сервер вернул неожиданный ответ.'
      )
    }
  })
})

describe('fetchAdminPortfolioPhotos', () => {
  it('шлёт pageSize, поиск и курсор в одном запросе', async () => {
    const captured = captureList(PORTFOLIO_URL)

    await fetchAdminPortfolioPhotos({
      search: 'соколов',
      cursor: 'cursor-2',
    })

    expect(entries(captured[0])).toStrictEqual([
      ['pageSize', String(ADMIN_PHOTOS_PAGE_SIZE)],
      ['search', 'соколов'],
      ['cursor', 'cursor-2'],
    ])
  })

  it('возвращает разобранную страницу с подписью и флагом публикации', async () => {
    server.use(
      http.get(PORTFOLIO_URL, () =>
        HttpResponse.json({
          items: [{ ...PORTFOLIO_ROW, description: null, isPublished: false }],
          nextCursor: null,
        })
      )
    )

    await expect(fetchAdminPortfolioPhotos({})).resolves.toStrictEqual({
      items: [{ ...PORTFOLIO_ROW, description: null, isPublished: false }],
      nextCursor: null,
    })
  })

  it('страница без moderationStatus не проходит guard', async () => {
    server.use(
      http.get(PORTFOLIO_URL, () =>
        HttpResponse.json({
          items: [{ ...PORTFOLIO_ROW, moderationStatus: 'Queued' }],
          nextCursor: null,
        })
      )
    )

    await expect(fetchAdminPortfolioPhotos({})).rejects.toBeInstanceOf(ApiError)
  })
})

describe('решения администратора', () => {
  function captureMutation(path: string) {
    const calls: {
      method: string
      path: string
      csrf: string | null
      body: string
    }[] = []

    server.use(
      http.post(path, async ({ request }) => {
        calls.push({
          method: request.method,
          path: new URL(request.url).pathname,
          csrf: request.headers.get('X-CSRF-TOKEN'),
          body: await request.text(),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    return calls
  }

  it('одобрение аватара — POST без тела', async () => {
    const calls = captureMutation(`/api/admin/users/${USER_ID}/avatar/approval`)

    await expect(approveAvatar(USER_ID)).resolves.toBeUndefined()

    expect(calls).toStrictEqual([
      {
        method: 'POST',
        path: `/api/admin/users/${USER_ID}/avatar/approval`,
        csrf: 'csrf-token',
        body: '',
      },
    ])
  })

  it('отклонение аватара — POST с причиной', async () => {
    const calls = captureMutation(`/api/admin/users/${USER_ID}/avatar/rejection`)

    await rejectAvatar(USER_ID, 'Кадр размыт')

    expect(calls[0].body).toBe(JSON.stringify({ reason: 'Кадр размыт' }))
  })

  it('одобрение работы портфолио — POST по паре profile/photo', async () => {
    const calls = captureMutation(
      `/api/barber-profiles/${BARBER_ID}/photos/${PHOTO_ID}/approval`
    )

    await approvePortfolioPhoto(BARBER_ID, PHOTO_ID)

    expect(calls).toStrictEqual([
      {
        method: 'POST',
        path: `/api/barber-profiles/${BARBER_ID}/photos/${PHOTO_ID}/approval`,
        csrf: 'csrf-token',
        body: '',
      },
    ])
  })

  it('отклонение работы портфолио — POST с причиной', async () => {
    const calls = captureMutation(
      `/api/barber-profiles/${BARBER_ID}/photos/${PHOTO_ID}/rejection`
    )

    await rejectPortfolioPhoto(BARBER_ID, PHOTO_ID, 'Не работа барбера')

    expect(calls[0].body).toBe(JSON.stringify({ reason: 'Не работа барбера' }))
  })
})

describe('describePhotoModerationError', () => {
  it('403 и 401 объясняют роль и сессию', () => {
    expect(describePhotoModerationError(apiError(403, 'auth.forbidden'))).toBe(
      'Раздел доступен только администратору.'
    )
    expect(describePhotoModerationError(apiError(401, 'auth.required'))).toBe(
      'Сессия истекла — войдите снова.'
    )
  })

  it('404 различает исчезнувшего пользователя и исчезнувший кадр', () => {
    expect(describePhotoModerationError(apiError(404, 'user.notFound'))).toBe(
      'Пользователь больше не существует. Обновите список.'
    )
    expect(
      describePhotoModerationError(apiError(404, 'barberProfile.notFound'))
    ).toBe('Фото больше не существует. Обновите список.')
    expect(
      describePhotoModerationError(
        apiError(404, 'portfolioPhoto.moderation.notFound')
      )
    ).toBe('Фото больше не существует. Обновите список.')
    expect(
      describePhotoModerationError(
        new ApiError(404, null, 'Not Found')
      )
    ).toBe('Фото больше не существует. Обновите список.')
  })

  it('409 говорит, что фото уже проверено', () => {
    const expected = 'Фото уже проверено. Обновите список.'

    expect(
      describePhotoModerationError(
        apiError(409, 'user.avatarModeration.invalidStatus')
      )
    ).toBe(expected)
    expect(
      describePhotoModerationError(
        apiError(409, 'portfolioPhoto.moderation.invalidStatus')
      )
    ).toBe(expected)
    expect(describePhotoModerationError(apiError(409, 'photo.changed'))).toBe(
      expected
    )
  })

  it('409 «нет аватара» — отдельный текст, а не «уже проверено»', () => {
    expect(
      describePhotoModerationError(
        apiError(409, 'user.avatarModeration.noAvatar')
      )
    ).toBe('У пользователя нет фото — проверять нечего.')
  })

  it('400 причины отказа объясняют, что исправить', () => {
    expect(
      describePhotoModerationError(
        apiError(400, 'user.avatarModeration.reasonRequired')
      )
    ).toBe('Укажите причину отказа.')
    expect(
      describePhotoModerationError(
        apiError(400, 'portfolioPhoto.moderation.reasonTooLong')
      )
    ).toBe('Слишком длинная причина — максимум 500 символов.')
  })

  it('429, сеть и неизвестный отказ дают русский текст', () => {
    expect(describePhotoModerationError(apiError(429, 'rateLimit.exceeded'))).toBe(
      'Слишком много действий подряд. Подождите минуту и повторите.'
    )
    expect(describePhotoModerationError(new ApiError(0, null, 'offline'))).toBe(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
    expect(describePhotoModerationError(new Error('boom'))).toBe(
      'Не удалось выполнить действие с фото. Попробуйте ещё раз.'
    )
  })

  it('никогда не показывает английский detail', () => {
    const error = new ApiError(
      400,
      problemDetails(400, { detail: 'Photo could not be moderated.' }),
      'Photo could not be moderated.'
    )

    expect(describePhotoModerationError(error)).toBe(
      'Не удалось выполнить действие с фото. Попробуйте ещё раз.'
    )
  })
})

describe('describeAdminPhotoListError', () => {
  it('403 — роль, битый курсор — начало списка', () => {
    expect(describeAdminPhotoListError(apiError(403, 'auth.forbidden'))).toBe(
      'Раздел доступен только администратору.'
    )
    expect(describeAdminPhotoListError(apiError(400, 'admin.photos.cursor'))).toBe(
      'Список устарел. Обновите его и продолжите с начала.'
    )
  })

  it('неизвестная ошибка получает запасной текст', () => {
    expect(describeAdminPhotoListError(new Error('boom'))).toBe(
      'Не удалось загрузить фотографии на проверке.'
    )
  })
})

describe('isAdminPhotoUnauthorized', () => {
  it('узнаёт только 401', () => {
    expect(isAdminPhotoUnauthorized(apiError(401, 'auth.required'))).toBe(true)
    expect(isAdminPhotoUnauthorized(apiError(403, 'auth.forbidden'))).toBe(false)
    expect(isAdminPhotoUnauthorized(new Error('boom'))).toBe(false)
  })
})
