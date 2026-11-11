import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  deleteAvatar,
  describeAvatarError,
  uploadAvatarWithStatus,
} from '@/lib/avatars'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

async function apiErrorOf(request: Promise<unknown>): Promise<ApiError> {
  try {
    await request
  } catch (error) {
    if (error instanceof ApiError) {
      return error
    }

    throw error
  }

  throw new Error('ожидался ApiError, но запрос завершился успешно')
}

function avatarFile(): File {
  return new File(['avatar-bytes'], 'avatar.jpg', { type: 'image/jpeg' })
}

describe('uploadAvatarWithStatus', () => {
  it('шлёт multipart с единственным полем file, свежий CSRF и возвращает URL со статусом', async () => {
    const append = vi.spyOn(FormData.prototype, 'append')
    const file = avatarFile()
    let method = ''
    let url = ''
    let csrf: string | null = null
    let contentType: string | null = null

    server.use(
      http.post('/api/users/me/avatar', ({ request }) => {
        method = request.method
        url = new URL(request.url).pathname
        csrf = request.headers.get('X-CSRF-TOKEN')
        contentType = request.headers.get('content-type')

        return HttpResponse.json(
          { avatarUrl: AVATAR_URL, moderationStatus: 'Pending' },
          { status: 201 }
        )
      })
    )

    await expect(uploadAvatarWithStatus(file)).resolves.toStrictEqual({
      avatarUrl: AVATAR_URL,
      moderationStatus: 'Pending',
    })

    expect(method).toBe('POST')
    expect(url).toBe('/api/users/me/avatar')
    expect(csrf).toBe('csrf-token')
    // JSON-заголовок сломал бы boundary — его не ставим (§9.1).
    expect(contentType).toMatch(/^multipart\/form-data; boundary=/)
    expect(append).toHaveBeenCalledTimes(1)
    expect(append.mock.calls[0]).toStrictEqual(['file', file])
  })

  it('на auth.csrf.invalid повторяет загрузку с тем же FormData и свежим токеном', async () => {
    const append = vi.spyOn(FormData.prototype, 'append')
    const csrfCalls = countRequests('/api/auth/csrf')
    const posts: { csrf: string | null }[] = []
    let csrfIndex = 0

    server.use(
      http.get('/api/auth/csrf', () => {
        csrfIndex += 1

        return HttpResponse.json({ token: `csrf-${csrfIndex}` })
      }),
      http.post('/api/users/me/avatar', ({ request }) => {
        posts.push({ csrf: request.headers.get('X-CSRF-TOKEN') })

        if (posts.length === 1) {
          return HttpResponse.json(
            problemDetails(400, { errorCodes: ['auth.csrf.invalid'] }),
            { status: 400 }
          )
        }

        return HttpResponse.json(
          { avatarUrl: AVATAR_URL, moderationStatus: 'Pending' },
          { status: 201 }
        )
      })
    )

    await expect(uploadAvatarWithStatus(avatarFile())).resolves.toStrictEqual({
      avatarUrl: AVATAR_URL,
      moderationStatus: 'Pending',
    })

    expect(csrfCalls()).toBe(2)
    expect(posts).toStrictEqual([{ csrf: 'csrf-1' }, { csrf: 'csrf-2' }])
    // FormData собрана один раз: повтор уходит с тем же объектом.
    expect(append).toHaveBeenCalledTimes(1)
  })

  it('отвергает пустой, чужой или внешний avatarUrl: guard строгий', async () => {
    for (const broken of [
      {},
      { avatarUrl: '' },
      { avatarUrl: '/api/media/portfolio/x.jpg' },
      { avatarUrl: 'https://example.com/avatar.jpg' },
      { avatarUrl: 1 },
      { avatarUrl: AVATAR_URL },
      { avatarUrl: AVATAR_URL, moderationStatus: 'Queued' },
    ]) {
      server.use(
        http.post('/api/users/me/avatar', () =>
          HttpResponse.json(broken, { status: 201 })
        )
      )

      const error = await apiErrorOf(uploadAvatarWithStatus(avatarFile()))

      expect(error.status).toBe(201)
      expect(error.message).toBe('Сервер вернул неожиданный ответ.')
    }
  })
})

describe('deleteAvatar', () => {
  it('шлёт DELETE без тела и с CSRF', async () => {
    let method = ''
    let url = ''
    let csrf: string | null = null
    let body = ''

    server.use(
      http.delete('/api/users/me/avatar', async ({ request }) => {
        method = request.method
        url = new URL(request.url).pathname
        csrf = request.headers.get('X-CSRF-TOKEN')
        body = await request.text()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await expect(deleteAvatar()).resolves.toBeUndefined()

    expect(method).toBe('DELETE')
    expect(url).toBe('/api/users/me/avatar')
    expect(csrf).toBe('csrf-token')
    expect(body).toBe('')
  })
})

/**
 * Разбор отказов: коды файла важнее статуса, у остальных веток — русский текст
 * и честный `canRetry` (`0`/`429`/`5xx` данные не меняли).
 */
describe('describeAvatarError', () => {
  function failure(code: string, status = 400) {
    return new ApiError(status, { status, errorCodes: [code] }, 'Detail')
  }

  it('привязывает общие коды портфолио к полю file', () => {
    expect(
      describeAvatarError(failure('barberProfile.uploadPhoto.fileRequired'))
    ).toStrictEqual({
      field: 'file',
      message: 'Выберите файл.',
      canRetry: false,
    })
    expect(
      describeAvatarError(failure('barberProfile.uploadPhoto.empty'))
    ).toStrictEqual({
      field: 'file',
      message: 'Изображение пустое или слишком маленькое. Выберите другой файл.',
      canRetry: false,
    })
    expect(
      describeAvatarError(failure('barberProfile.uploadPhoto.tooLarge'))
    ).toStrictEqual({
      field: 'file',
      message: 'Файл слишком большой. Максимум — 5 МБ.',
      canRetry: false,
    })
    expect(
      describeAvatarError(failure('barberProfile.uploadPhoto.unsupportedType'))
    ).toStrictEqual({
      field: 'file',
      message: 'Поддерживаются только JPEG, PNG и WebP.',
      canRetry: false,
    })
    expect(
      describeAvatarError(failure('barberProfile.uploadPhoto.invalidImage'))
    ).toStrictEqual({
      field: 'file',
      message: 'Не удалось прочитать изображение. Выберите другой файл.',
      canRetry: false,
    })
    expect(
      describeAvatarError(failure('barberProfile.uploadPhoto.dimensions'))
    ).toStrictEqual({
      field: 'file',
      message: 'Стороны фото должны быть от 300 до 6000 px.',
      canRetry: false,
    })
  })

  it('привязывает новые коды загрузки аватара к полю file', () => {
    expect(
      describeAvatarError(failure('user.uploadAvatar.fileRequired'))
    ).toStrictEqual({
      field: 'file',
      message: 'Выберите файл.',
      canRetry: false,
    })
    expect(
      describeAvatarError(failure('user.uploadAvatar.tooLarge'))
    ).toStrictEqual({
      field: 'file',
      message: 'Файл слишком большой. Максимум — 5 МБ.',
      canRetry: false,
    })
  })

  it('413 от Kestrel без ProblemDetails — тоже ошибка файла', () => {
    expect(
      describeAvatarError(new ApiError(413, null, 'Payload Too Large'))
    ).toStrictEqual({
      field: 'file',
      message: 'Файл слишком большой. Максимум — 5 МБ.',
      canRetry: false,
    })
  })

  it('сбой хранилища — серверная ошибка с безопасным повтором', () => {
    expect(
      describeAvatarError(failure('user.uploadAvatar.storage', 500))
    ).toStrictEqual({
      field: null,
      message: 'Не удалось сохранить файл. Попробуйте ещё раз.',
      canRetry: true,
    })
  })

  it('401 не предлагает повтор, 429 и сеть предлагают', () => {
    expect(describeAvatarError(failure('auth.unauthorized', 401))).toStrictEqual(
      {
        field: null,
        message: 'Сессия истекла — войдите снова.',
        canRetry: false,
      }
    )
    expect(describeAvatarError(failure('rateLimit.exceeded', 429))).toStrictEqual(
      {
        field: null,
        message: 'Слишком много запросов. Подождите минуту и повторите.',
        canRetry: true,
      }
    )
    expect(describeAvatarError(new ApiError(0, null, 'offline'))).toStrictEqual({
      field: null,
      message: 'Нет связи с сервером. Проверьте подключение и повторите попытку.',
      canRetry: true,
    })
  })

  it('5xx без кода — русский fallback с повтором, 4xx — без', () => {
    expect(describeAvatarError(failure('boom', 503))).toStrictEqual({
      field: null,
      message: 'Не удалось изменить аватар. Попробуйте ещё раз.',
      canRetry: true,
    })
    expect(describeAvatarError(failure('unknown', 400))).toStrictEqual({
      field: null,
      message: 'Не удалось изменить аватар. Попробуйте ещё раз.',
      canRetry: false,
    })
  })

  it('не-ApiError даёт fallback и не показывает английский detail', () => {
    expect(describeAvatarError(new Error('boom'))).toStrictEqual({
      field: null,
      message: 'Не удалось изменить аватар. Попробуйте ещё раз.',
      canRetry: false,
    })
  })
})
