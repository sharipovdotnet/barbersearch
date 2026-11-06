import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import {
  ADMIN_BARBER_REQUESTS_PAGE_SIZE,
  ADMIN_BARBER_REQUEST_FILTERS,
  BARBER_REQUEST_REJECTION_MAX_LENGTH,
  approveBarberRequest,
  describeAdminBarberRequestActionError,
  describeAdminBarberRequestDetailError,
  describeAdminBarberRequestListError,
  fetchAdminBarberRequest,
  fetchAdminBarberRequests,
  isBarberRequestGone,
  isBarberRequestUnauthorized,
  rejectBarberRequest,
  type AdminBarberRequest,
} from '@/lib/admin-barber-requests'
import { ApiError } from '@/lib/api'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Транспорт, guard'ы и ошибки админской очереди заявок.
 *
 * Контракт списка: `status` уходит всегда, `cursor` — только следующим страницам,
 * `pageSize` — ровно дефолт сервера. `total` нет, поэтому экран показывает диапазон
 * из `ListPager`. Оба решения — cookie-мутации с `X-CSRF-TOKEN`, отказ несёт причину.
 */

const REQUESTS_URL = '/api/admin/barber-requests'
const REQUEST_ID = '9ea00000-0000-4000-8000-000000000001'
const REQUEST_URL = `${REQUESTS_URL}/${REQUEST_ID}`

const REQUEST: AdminBarberRequest = {
  id: REQUEST_ID,
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

function captureList(): URLSearchParams[] {
  const captured: URLSearchParams[] = []

  server.use(
    http.get(REQUESTS_URL, ({ request }) => {
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

describe('fetchAdminBarberRequests', () => {
  it('по умолчанию открывает очередь: status=pending и pageSize', async () => {
    const captured = captureList()

    await fetchAdminBarberRequests({ status: 'pending' })

    expect(entries(captured[0])).toStrictEqual([
      ['status', 'pending'],
      ['pageSize', String(ADMIN_BARBER_REQUESTS_PAGE_SIZE)],
    ])
  })

  it('все заявки: status=all', async () => {
    const captured = captureList()

    await fetchAdminBarberRequests({ status: 'all' })

    expect(captured[0].get('status')).toBe('all')
  })

  it('cursor добавляется последним и только когда он есть', async () => {
    const captured = captureList()

    await fetchAdminBarberRequests({ status: 'all', cursor: 'cursor-2' })

    expect(entries(captured[0])).toStrictEqual([
      ['status', 'all'],
      ['pageSize', String(ADMIN_BARBER_REQUESTS_PAGE_SIZE)],
      ['cursor', 'cursor-2'],
    ])

    await fetchAdminBarberRequests({ status: 'pending', cursor: null })

    expect(captured[1].has('cursor')).toBe(false)
  })

  it('фильтры — ровно очередь и все заявки', () => {
    expect(ADMIN_BARBER_REQUEST_FILTERS).toStrictEqual(['pending', 'all'])
  })

  it('guard принимает лишние поля и отвергает неизвестный статус', async () => {
    server.use(
      http.get(REQUESTS_URL, () =>
        HttpResponse.json({
          items: [{ ...REQUEST, extra: 'ignored' }],
          nextCursor: null,
        })
      )
    )

    await expect(
      fetchAdminBarberRequests({ status: 'pending' })
    ).resolves.toStrictEqual({
      items: [{ ...REQUEST, extra: 'ignored' }],
      nextCursor: null,
    })

    server.use(
      http.get(REQUESTS_URL, () =>
        HttpResponse.json({
          items: [{ ...REQUEST, status: 'Unknown' }],
          nextCursor: null,
        })
      )
    )

    await expect(
      fetchAdminBarberRequests({ status: 'pending' })
    ).rejects.toBeInstanceOf(ApiError)
  })

  it('guard требует заявителя у админской строки', async () => {
    server.use(
      http.get(REQUESTS_URL, () =>
        HttpResponse.json({
          items: [{ ...REQUEST, applicant: undefined }],
          nextCursor: null,
        })
      )
    )

    await expect(
      fetchAdminBarberRequests({ status: 'pending' })
    ).rejects.toBeInstanceOf(ApiError)
  })
})

describe('fetchAdminBarberRequest', () => {
  it('читает карточку по id', async () => {
    const urls: string[] = []

    server.use(
      http.get(REQUEST_URL, ({ request }) => {
        urls.push(request.url)

        return HttpResponse.json(REQUEST)
      })
    )

    await expect(fetchAdminBarberRequest(REQUEST_ID)).resolves.toStrictEqual(
      REQUEST
    )
    expect(urls).toHaveLength(1)
  })
})

describe('approveBarberRequest', () => {
  it('отправляет POST с CSRF-заголовком и без тела', async () => {
    const calls: { csrf: string | null; body: string }[] = []

    server.use(
      http.post(`${REQUEST_URL}/approval`, async ({ request }) => {
        calls.push({
          csrf: request.headers.get('X-CSRF-TOKEN'),
          body: await request.text(),
        })

        return HttpResponse.json({ ...REQUEST, status: 'Approved' })
      })
    )

    const updated = await approveBarberRequest(REQUEST_ID)

    expect(updated.status).toBe('Approved')
    expect(calls).toHaveLength(1)
    expect(calls[0].csrf).toBe('csrf-token')
    expect(calls[0].body).toBe('')
  })

  it('повторяет один раз на устаревший CSRF-токен', async () => {
    let attempts = 0

    server.use(
      http.post(`${REQUEST_URL}/approval`, () => {
        attempts += 1

        if (attempts === 1) {
          return HttpResponse.json(
            problemDetails(400, { errorCodes: ['auth.csrf.invalid'] }),
            { status: 400 }
          )
        }

        return HttpResponse.json({ ...REQUEST, status: 'Approved' })
      })
    )

    await approveBarberRequest(REQUEST_ID)

    expect(attempts).toBe(2)
  })
})

describe('rejectBarberRequest', () => {
  it('передаёт причину в теле и CSRF-заголовок', async () => {
    const bodies: string[] = []

    server.use(
      http.post(`${REQUEST_URL}/rejection`, async ({ request }) => {
        bodies.push(await request.text())

        return HttpResponse.json({
          ...REQUEST,
          status: 'Rejected',
          rejectionReason: 'Не хватает опыта',
        })
      })
    )

    const updated = await rejectBarberRequest(REQUEST_ID, 'Не хватает опыта')

    expect(updated.status).toBe('Rejected')
    expect(bodies).toStrictEqual([
      JSON.stringify({ reason: 'Не хватает опыта' }),
    ])
  })
})

describe('ошибки админских заявок', () => {
  it('список: 403 — текст про роль, 429 — про лимит', () => {
    expect(
      describeAdminBarberRequestListError(apiError(403, 'auth.forbidden'))
    ).toBe('Раздел доступен только администратору.')
    expect(
      describeAdminBarberRequestListError(apiError(429, 'rate.limit'))
    ).toBe('Слишком много запросов. Подождите минуту и повторите.')
    expect(describeAdminBarberRequestListError(new Error('boom'))).toBe(
      'Не удалось загрузить заявки.'
    )
  })

  it('карточка: 404 — заявка не найдена', () => {
    expect(
      describeAdminBarberRequestDetailError(
        apiError(404, 'barberRequest.notFound')
      )
    ).toBe('Заявка не найдена. Возможно, ссылка устарела.')
    expect(
      describeAdminBarberRequestDetailError(apiError(403, 'auth.forbidden'))
    ).toBe('Раздел доступен только администратору.')
  })

  it('действия: уже разобрали, причина и лимит длины', () => {
    expect(
      describeAdminBarberRequestActionError(
        apiError(409, 'barberRequest.review.invalidStatus'),
        'approve'
      )
    ).toBe('Заявку уже рассмотрели. Обновите список.')
    expect(
      describeAdminBarberRequestActionError(
        apiError(409, 'barberRequest.review.invalidApplicant'),
        'approve'
      )
    ).toBe(
      'Роль заявителя уже изменилась — заявку нельзя одобрить автоматически. Обновите список.'
    )
    expect(
      describeAdminBarberRequestActionError(
        apiError(400, 'barberRequest.rejection.reasonRequired'),
        'reject'
      )
    ).toBe('Укажите причину отказа.')
    expect(
      describeAdminBarberRequestActionError(
        apiError(400, 'barberRequest.rejection.reasonTooLong'),
        'reject'
      )
    ).toBe(
      `Слишком длинная причина — максимум ${BARBER_REQUEST_REJECTION_MAX_LENGTH} символов.`
    )
    expect(
      describeAdminBarberRequestActionError(apiError(404, 'barberRequest.notFound'), 'reject')
    ).toBe('Заявка больше не существует. Обновите список.')
  })

  it('фолбэк действия зависит от решения', () => {
    expect(describeAdminBarberRequestActionError(new Error('boom'), 'approve')).toBe(
      'Не удалось одобрить заявку. Попробуйте ещё раз.'
    )
    expect(describeAdminBarberRequestActionError(new Error('boom'), 'reject')).toBe(
      'Не удалось отклонить заявку. Попробуйте ещё раз.'
    )
  })

  it('401 и 404 распознаются для гейта и локального удаления строки', () => {
    expect(isBarberRequestUnauthorized(apiError(401, 'auth.required'))).toBe(
      true
    )
    expect(isBarberRequestGone(apiError(404, 'barberRequest.notFound'))).toBe(
      true
    )
    expect(isBarberRequestGone(apiError(409, 'barberRequest.review.invalidStatus'))).toBe(
      false
    )
  })
})
