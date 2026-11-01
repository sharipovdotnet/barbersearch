import { http, HttpResponse } from 'msw'
import { afterEach, describe, expect, it } from 'vitest'

import { ApiError, apiRequest, setUnauthorizedHandler } from '@/lib/api'
import { isSessionUser } from '@/lib/response-guards'
import { noContent, problemDetails, unauthorized } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/** Гарантированный `ApiError` в тесте: иначе падаем с внятным сообщением. */
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

describe('apiRequest', () => {
  // Обработчик `401` — модульное состояние: без сброса тесты влияют друг на друга.
  afterEach(() => {
    setUnauthorizedHandler(null)
  })

  it('возвращает тело 200 как есть', async () => {
    server.use(
      http.get('/api/thing', () =>
        HttpResponse.json({ id: '1', nested: { ok: true } })
      )
    )

    const body = await apiRequest<{ id: string; nested: { ok: boolean } }>(
      '/thing'
    )

    expect(body).toStrictEqual({ id: '1', nested: { ok: true } })
  })

  it('на 204 без тела отдаёт null', async () => {
    server.use(http.delete('/api/thing', () => noContent()))

    await expect(apiRequest('/thing', { method: 'DELETE' })).resolves.toBeNull()
  })

  it('на 200 с невалидным JSON отдаёт null — документированное поведение readJson', async () => {
    server.use(
      http.get('/api/broken', () =>
        new HttpResponse('<html>not json</html>', {
          status: 200,
          headers: { 'Content-Type': 'text/html' },
        })
      )
    )

    await expect(apiRequest('/broken')).resolves.toBeNull()
  })

  it('превращает non-2xx ProblemDetails в ApiError с кодами и текстом detail', async () => {
    const problem = problemDetails(409, {
      detail: 'Barber already has a booking in this slot.',
      errorCodes: ['booking.overlap'],
    })

    server.use(
      http.post('/api/bookings', () => HttpResponse.json(problem, { status: 409 }))
    )

    const error = await apiErrorOf(
      apiRequest('/bookings', { method: 'POST', body: '{}' })
    )

    expect(error.status).toBe(409)
    expect(error.problem).toStrictEqual(problem)
    expect(error.codes).toStrictEqual(['booking.overlap'])
    expect(error.hasCode('booking.overlap')).toBe(true)
    expect(error.hasCode('booking.unknown')).toBe(false)
    expect(error.message).toBe('Barber already has a booking in this slot.')
  })

  it('без detail отдаёт нейтральный русский текст', async () => {
    server.use(
      http.get('/api/thing', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    const error = await apiErrorOf(apiRequest('/thing'))

    expect(error.status).toBe(500)
    expect(error.message).toBe('Запрос не выполнен.')
  })

  it('на HTML вместо ProblemDetails не доверяет телу: problem null, codes пусты', async () => {
    server.use(
      http.get('/api/thing', () =>
        new HttpResponse('<html>500</html>', {
          status: 500,
          headers: { 'Content-Type': 'text/html' },
        })
      )
    )

    const error = await apiErrorOf(apiRequest('/thing'))

    expect(error.problem).toBeNull()
    expect(error.codes).toStrictEqual([])
    expect(error.message).toBe('Запрос не выполнен.')
  })

  it('на JSON-массив в теле ошибки не доверяет телу: problem null', async () => {
    server.use(
      http.get('/api/thing', () =>
        HttpResponse.json([{ errorCodes: ['fake.code'] }], { status: 400 })
      )
    )

    const error = await apiErrorOf(apiRequest('/thing'))

    expect(error.problem).toBeNull()
    expect(error.codes).toStrictEqual([])
    expect(error.hasCode('fake.code')).toBe(false)
  })

  it('на недоступной сети отдаёт статус 0 и текст про связь', async () => {
    server.use(http.get('/api/thing', () => HttpResponse.error()))

    const error = await apiErrorOf(apiRequest('/thing'))

    expect(error.status).toBe(0)
    expect(error.problem).toBeNull()
    expect(error.message).toBe('Нет связи с сервером.')
  })

  it('на непрошедшее validate тело отдаёт ApiError с фактическим статусом', async () => {
    server.use(
      http.post('/api/thing', () =>
        HttpResponse.json({ id: 1 }, { status: 201 })
      )
    )

    const error = await apiErrorOf(
      apiRequest('/thing', { method: 'POST', body: '{}' }, { validate: isSessionUser })
    )

    expect(error.status).toBe(201)
    expect(error.problem).toBeNull()
    expect(error.codes).toStrictEqual([])
    expect(error.message).toBe('Сервер вернул неожиданный ответ.')
  })

  it('на 401 без зарегистрированного обработчика бросает ошибку без повтора', async () => {
    const requests = countRequests('/api/bookings')

    server.use(http.get('/api/bookings', () => unauthorized()))
    setUnauthorizedHandler(null)

    const error = await apiErrorOf(apiRequest('/bookings'))

    expect(error.status).toBe(401)
    expect(error.hasCode('auth.required')).toBe(true)
    expect(requests()).toBe(1)
  })

  it('после 401 с решением retry повторяет запрос ровно один раз и отдаёт успех', async () => {
    let attempts = 0

    server.use(
      http.get('/api/bookings', () => {
        attempts += 1

        return attempts === 1
          ? unauthorized()
          : HttpResponse.json([{ id: 'booking-1' }])
      })
    )
    setUnauthorizedHandler(async () => ({ retry: true }))

    await expect(apiRequest('/bookings')).resolves.toStrictEqual([
      { id: 'booking-1' },
    ])
    expect(attempts).toBe(2)
  })

  it('после 401 с решением retry false сразу отдаёт исходную ошибку', async () => {
    const requests = countRequests('/api/bookings')

    server.use(
      http.get('/api/bookings', () =>
        HttpResponse.json(
          problemDetails(401, {
            detail: 'Session expired.',
            errorCodes: ['auth.required'],
          }),
          { status: 401 }
        )
      )
    )
    setUnauthorizedHandler(async () => ({ retry: false }))

    const error = await apiErrorOf(apiRequest('/bookings'))

    expect(error.status).toBe(401)
    expect(error.message).toBe('Session expired.')
    expect(requests()).toBe(1)
  })

  it('не продлевает сессию на 401 от /auth/*', async () => {
    const requests = countRequests('/api/auth/me')
    let handlerCalls = 0

    server.use(http.get('/api/auth/me', () => unauthorized()))
    setUnauthorizedHandler(async () => {
      handlerCalls += 1
      return { retry: true }
    })

    const error = await apiErrorOf(apiRequest('/auth/me'))

    expect(error.status).toBe(401)
    expect(handlerCalls).toBe(0)
    expect(requests()).toBe(1)
  })

  it('не продлевает сессию при retryOnUnauthorized: false', async () => {
    const requests = countRequests('/api/bookings')
    let handlerCalls = 0

    server.use(http.get('/api/bookings', () => unauthorized()))
    setUnauthorizedHandler(async () => {
      handlerCalls += 1
      return { retry: true }
    })

    const error = await apiErrorOf(
      apiRequest('/bookings', {}, { retryOnUnauthorized: false })
    )

    expect(error.status).toBe(401)
    expect(handlerCalls).toBe(0)
    expect(requests()).toBe(1)
  })
})
