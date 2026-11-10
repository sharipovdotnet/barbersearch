import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  blockPortfolioPhoto,
  describePortfolioPhotoError,
  publishPortfolioPhoto,
  removePortfolioPhoto,
  reorderPortfolioPhotos,
  unblockPortfolioPhoto,
  unpublishPortfolioPhoto,
  updatePortfolioPhotoDescription,
  uploadPortfolioPhoto,
} from '@/lib/portfolio-photos'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const PROFILE_ID = '9e100000-0000-4000-8000-000000000015'
const PHOTO_ID = '9e200000-0000-4000-8000-000000000001'
const BASE = `/api/barber-profiles/${PROFILE_ID}/photos`

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

/**
 * Тело multipart проверяем через `FormData.append`: чтение такого тела в MSW
 * (`request.text()`/`formData()`) зависает на jsdom-овом `File` — это ограничение
 * тестового окружения, а не транспорта. Заголовки запроса при этом видны как обычно.
 */
function photoFile(): File {
  return new File(['photo-bytes'], 'IMG_0001.jpeg', { type: 'image/jpeg' })
}

describe('uploadPortfolioPhoto', () => {
  it('шлёт multipart с полями file и description, свежий CSRF и возвращает id', async () => {
    const append = vi.spyOn(FormData.prototype, 'append')
    const file = photoFile()
    let method = ''
    let url = ''
    let csrf: string | null = null
    let contentType: string | null = null

    server.use(
      http.post(`${BASE}/upload`, ({ request }) => {
        method = request.method
        url = new URL(request.url).pathname
        csrf = request.headers.get('X-CSRF-TOKEN')
        contentType = request.headers.get('content-type')

        return HttpResponse.json({ id: PHOTO_ID }, { status: 201 })
      })
    )

    await expect(
      uploadPortfolioPhoto(PROFILE_ID, file, '  Работа  ')
    ).resolves.toBe(PHOTO_ID)

    expect(method).toBe('POST')
    expect(url).toBe(`${BASE}/upload`)
    expect(csrf).toBe('csrf-token')
    // JSON-заголовок сломал бы boundary — его не ставим (§8).
    expect(contentType).toMatch(/^multipart\/form-data; boundary=/)
    expect(append).toHaveBeenCalledTimes(2)
    expect(append.mock.calls[0]).toStrictEqual(['file', file])
    expect(append.mock.calls[1]).toStrictEqual(['description', 'Работа'])
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
      http.post(`${BASE}/upload`, ({ request }) => {
        posts.push({ csrf: request.headers.get('X-CSRF-TOKEN') })

        if (posts.length === 1) {
          return HttpResponse.json(
            problemDetails(400, { errorCodes: ['auth.csrf.invalid'] }),
            { status: 400 }
          )
        }

        return HttpResponse.json({ id: PHOTO_ID }, { status: 201 })
      })
    )

    await expect(
      uploadPortfolioPhoto(PROFILE_ID, photoFile(), 'Подпись')
    ).resolves.toBe(PHOTO_ID)

    expect(csrfCalls()).toBe(2)
    expect(posts).toStrictEqual([{ csrf: 'csrf-1' }, { csrf: 'csrf-2' }])
    // FormData собрана один раз: повтор уходит с тем же объектом, а не новым телом.
    expect(append).toHaveBeenCalledTimes(2)
  })

  it('отвергает 201 без id: битый ответ не должен притворяться созданным фото', async () => {
    server.use(
      http.post(`${BASE}/upload`, () =>
        HttpResponse.json({ id: 1 }, { status: 201 })
      )
    )

    const error = await apiErrorOf(
      uploadPortfolioPhoto(PROFILE_ID, photoFile(), null)
    )

    expect(error.status).toBe(201)
    expect(error.message).toBe('Сервер вернул неожиданный ответ.')
  })
})

describe('остальные мутации портфолио', () => {
  it('updatePortfolioPhotoDescription шлёт PUT с обрезанной подписью', async () => {
    let method = ''
    let url = ''
    let csrf: string | null = null
    let body = ''

    server.use(
      http.put(`${BASE}/${PHOTO_ID}/description`, async ({ request }) => {
        method = request.method
        url = new URL(request.url).pathname
        csrf = request.headers.get('X-CSRF-TOKEN')
        body = await request.text()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await updatePortfolioPhotoDescription(PROFILE_ID, PHOTO_ID, '  Новая  ')

    expect(method).toBe('PUT')
    expect(url).toBe(`${BASE}/${PHOTO_ID}/description`)
    expect(csrf).toBe('csrf-token')
    expect(JSON.parse(body)).toStrictEqual({ description: 'Новая' })
  })

  it('пустая подпись уходит как null', async () => {
    let body = ''

    server.use(
      http.put(`${BASE}/${PHOTO_ID}/description`, async ({ request }) => {
        body = await request.text()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await updatePortfolioPhotoDescription(PROFILE_ID, PHOTO_ID, '   ')

    expect(JSON.parse(body)).toStrictEqual({ description: null })
  })

  it('removePortfolioPhoto удаляет фотографию и не шлёт тело', async () => {
    let method = ''
    let url = ''
    let csrf: string | null = null
    let body: string | undefined

    server.use(
      http.delete(`${BASE}/${PHOTO_ID}`, async ({ request }) => {
        method = request.method
        url = new URL(request.url).pathname
        csrf = request.headers.get('X-CSRF-TOKEN')
        body = await request.text()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await removePortfolioPhoto(PROFILE_ID, PHOTO_ID)

    expect(method).toBe('DELETE')
    expect(url).toBe(`${BASE}/${PHOTO_ID}`)
    expect(csrf).toBe('csrf-token')
    expect(body).toBe('')
  })

  it('публикация — POST, снятие — DELETE на одном маршруте', async () => {
    const calls: { method: string; url: string }[] = []

    server.use(
      http.post(`${BASE}/${PHOTO_ID}/publication`, ({ request }) => {
        calls.push({
          method: request.method,
          url: new URL(request.url).pathname,
        })

        return new HttpResponse(null, { status: 204 })
      }),
      http.delete(`${BASE}/${PHOTO_ID}/publication`, ({ request }) => {
        calls.push({
          method: request.method,
          url: new URL(request.url).pathname,
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    await publishPortfolioPhoto(PROFILE_ID, PHOTO_ID)
    await unpublishPortfolioPhoto(PROFILE_ID, PHOTO_ID)

    expect(calls).toStrictEqual([
      { method: 'POST', url: `${BASE}/${PHOTO_ID}/publication` },
      { method: 'DELETE', url: `${BASE}/${PHOTO_ID}/publication` },
    ])
  })

  it('reorderPortfolioPhotos шлёт полный массив id одним PUT', async () => {
    let method = ''
    let csrf: string | null = null
    let body = ''
    const photoIds = [PHOTO_ID, '9e200000-0000-4000-8000-000000000002']

    server.use(
      http.put(`${BASE}/order`, async ({ request }) => {
        method = request.method
        csrf = request.headers.get('X-CSRF-TOKEN')
        body = await request.text()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await reorderPortfolioPhotos(PROFILE_ID, photoIds)

    expect(method).toBe('PUT')
    expect(csrf).toBe('csrf-token')
    expect(JSON.parse(body)).toStrictEqual({ photoIds })
  })

  it('блокировка — POST, разблокировка — DELETE на одном маршруте', async () => {
    const calls: { method: string; url: string; csrf: string | null }[] = []

    server.use(
      http.post(`${BASE}/${PHOTO_ID}/block`, ({ request }) => {
        calls.push({
          method: request.method,
          url: new URL(request.url).pathname,
          csrf: request.headers.get('X-CSRF-TOKEN'),
        })

        return new HttpResponse(null, { status: 204 })
      }),
      http.delete(`${BASE}/${PHOTO_ID}/block`, ({ request }) => {
        calls.push({
          method: request.method,
          url: new URL(request.url).pathname,
          csrf: request.headers.get('X-CSRF-TOKEN'),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    await blockPortfolioPhoto(PROFILE_ID, PHOTO_ID)
    await unblockPortfolioPhoto(PROFILE_ID, PHOTO_ID)

    expect(calls).toStrictEqual([
      { method: 'POST', url: `${BASE}/${PHOTO_ID}/block`, csrf: 'csrf-token' },
      {
        method: 'DELETE',
        url: `${BASE}/${PHOTO_ID}/block`,
        csrf: 'csrf-token',
      },
    ])
  })
})

/**
 * Ошибки: коды §4.8 важнее статуса там, где у поля своя подсказка.
 *
 * `describePortfolioPhotoError` возвращает `field` — UI показывает текст у поля
 * `file`/`description`, а `null` — общей строкой над сеткой.
 */
describe('describePortfolioPhotoError', () => {
  function failure(code: string, status = 400) {
    return new ApiError(status, { status, errorCodes: [code] }, 'Detail')
  }

  it('привязывает отказы файла к полю file', () => {
    expect(describePortfolioPhotoError(failure('barberProfile.uploadPhoto.fileRequired'))).toStrictEqual({
      field: 'file',
      message: 'Выберите файл.',
      canRetry: false,
    })
    expect(describePortfolioPhotoError(failure('barberProfile.uploadPhoto.tooLarge'))).toStrictEqual({
      field: 'file',
      message: 'Файл слишком большой. Максимум — 5 МБ.',
      canRetry: false,
    })
    expect(describePortfolioPhotoError(failure('barberProfile.uploadPhoto.unsupportedType'))).toStrictEqual({
      field: 'file',
      message: 'Поддерживаются только JPEG, PNG и WebP.',
      canRetry: false,
    })
    expect(describePortfolioPhotoError(failure('barberProfile.uploadPhoto.invalidImage'))).toStrictEqual({
      field: 'file',
      message: 'Не удалось прочитать изображение. Выберите другой файл.',
      canRetry: false,
    })
    expect(describePortfolioPhotoError(failure('barberProfile.uploadPhoto.dimensions'))).toStrictEqual({
      field: 'file',
      message: 'Стороны фото должны быть от 300 до 6000 px.',
      canRetry: false,
    })
  })

  it('413 от Kestrel без ProblemDetails — тоже ошибка файла', () => {
    expect(
      describePortfolioPhotoError(new ApiError(413, null, 'Payload Too Large'))
    ).toStrictEqual({
      field: 'file',
      message: 'Файл слишком большой. Максимум — 5 МБ.',
      canRetry: false,
    })
  })

  it('длинная подпись — у поля description', () => {
    expect(
      describePortfolioPhotoError(failure('barberProfile.addPhoto.description'))
    ).toStrictEqual({
      field: 'description',
      message: 'Подпись не длиннее 2000 символов.',
      canRetry: false,
    })
  })

  it('лимит и неполный порядок — общие ошибки списка', () => {
    expect(
      describePortfolioPhotoError(failure('barberProfile.addPhoto.limit', 409))
    ).toStrictEqual({
      field: null,
      message: 'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.',
      canRetry: false,
    })
    expect(
      describePortfolioPhotoError(
        failure('barberProfile.reorderPhotos.invalid')
      )
    ).toStrictEqual({
      field: null,
      message: 'Не удалось сохранить порядок. Обновите страницу.',
      canRetry: false,
    })
  })

  it('исчезнувшее фото и чужая карточка не прячутся за fallback', () => {
    expect(
      describePortfolioPhotoError(failure('portfolioPhoto.block.notFound', 404))
    ).toStrictEqual({
      field: null,
      message: 'Фото не найдено. Обновите страницу.',
      canRetry: false,
    })
    expect(
      describePortfolioPhotoError(failure('portfolioPhoto.unblock.notFound', 404))
    ).toStrictEqual({
      field: null,
      message: 'Фото не найдено. Обновите страницу.',
      canRetry: false,
    })
    expect(
      describePortfolioPhotoError(failure('auth.forbidden', 403))
    ).toStrictEqual({
      field: null,
      message: 'Нет доступа к портфолио этого барбера.',
      canRetry: false,
    })
  })

  it('сбой хранилища — серверная ошибка с безопасным повтором', () => {
    expect(
      describePortfolioPhotoError(
        failure('barberProfile.uploadPhoto.storage', 500)
      )
    ).toStrictEqual({
      field: null,
      message: 'Не удалось сохранить файл. Попробуйте ещё раз.',
      canRetry: true,
    })
  })

  it('401, 429 и сеть объясняет общий разбор транспорта', () => {
    expect(
      describePortfolioPhotoError(failure('auth.unauthorized', 401))
    ).toStrictEqual({
      field: null,
      message: 'Сессия истекла — войдите снова.',
      canRetry: false,
    })
    expect(
      describePortfolioPhotoError(failure('rateLimit.exceeded', 429))
    ).toStrictEqual({
      field: null,
      message: 'Слишком много запросов. Подождите минуту и повторите.',
      canRetry: true,
    })
    expect(
      describePortfolioPhotoError(new ApiError(0, null, 'offline'))
    ).toStrictEqual({
      field: null,
      message: 'Нет связи с сервером. Проверьте подключение и повторите попытку.',
      canRetry: true,
    })
  })

  it('неизвестный код и не-ApiError дают общий fallback без повтора', () => {
    const fallback = 'Не удалось выполнить действие с фото. Попробуйте ещё раз.'

    expect(
      describePortfolioPhotoError(failure('barberProfile.uploadPhoto.url'))
    ).toStrictEqual({ field: null, message: fallback, canRetry: false })
    expect(describePortfolioPhotoError(new Error('boom'))).toStrictEqual({
      field: null,
      message: fallback,
      canRetry: false,
    })
  })
})
