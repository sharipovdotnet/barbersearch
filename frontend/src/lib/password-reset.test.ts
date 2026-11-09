import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  confirmPasswordReset,
  describePasswordResetFailure,
  requestPasswordReset,
} from '@/lib/password-reset'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const FALLBACK = 'Не удалось сохранить пароль. Попробуйте позже.'

describe('password-reset: транспорт', () => {
  it('запрос ссылки отправляет email без CSRF-запроса и заголовка', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let header: string | null = 'unset'
    let payload: unknown = null

    server.use(
      http.post('/api/auth/password-reset', async ({ request }) => {
        header = request.headers.get('x-csrf-token')
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await requestPasswordReset('client@barbersearch.test')

    expect(header).toBeNull()
    expect(payload).toEqual({ email: 'client@barbersearch.test' })
    expect(csrfCalls()).toBe(0)
  })

  it('подтверждение отправляет токен и пароль без CSRF-запроса и заголовка', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let header: string | null = 'unset'
    let payload: unknown = null

    server.use(
      http.post('/api/auth/password-reset/confirm', async ({ request }) => {
        header = request.headers.get('x-csrf-token')
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await confirmPasswordReset('token-123', 'new-secret-pass')

    expect(header).toBeNull()
    expect(payload).toEqual({
      token: 'token-123',
      newPassword: 'new-secret-pass',
    })
    expect(csrfCalls()).toBe(0)
  })
})

describe('password-reset: коды ошибок', () => {
  it('недействительная ссылка', () => {
    const failure = describePasswordResetFailure(
      new ApiError(
        400,
        { errorCodes: ['auth.passwordReset.invalid'] },
        'Bad Request'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('invalid')
    expect(failure.message).toBe('Ссылка недействительна или устарела.')
  })

  it('сброс по почте не настроен', () => {
    const failure = describePasswordResetFailure(
      new ApiError(
        409,
        { errorCodes: ['auth.passwordReset.unavailable'] },
        'Conflict'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('unavailable')
    expect(failure.message).toBe('Сброс пароля по почте пока не настроен.')
  })

  it('слишком много попыток', () => {
    const failure = describePasswordResetFailure(
      new ApiError(429, null, 'Too Many Requests'),
      FALLBACK
    )

    expect(failure.kind).toBe('rateLimited')
    expect(failure.message).toBe(
      'Слишком много попыток. Подождите минуту и повторите.'
    )
  })

  it('сеть', () => {
    const failure = describePasswordResetFailure(
      new ApiError(0, null, 'Network'),
      FALLBACK
    )

    expect(failure.kind).toBe('connection')
    expect(failure.message).toBe(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
  })

  it('5xx и неизвестная ошибка получают запасной текст', () => {
    const byStatus = describePasswordResetFailure(
      new ApiError(503, null, 'Service Unavailable'),
      FALLBACK
    )
    const byCode = describePasswordResetFailure(
      new ApiError(400, { errorCodes: ['auth.csrf.invalid'] }, 'Bad Request'),
      FALLBACK
    )
    const notApiError = describePasswordResetFailure(new Error('boom'), FALLBACK)

    expect(byStatus.kind).toBe('unknown')
    expect(byStatus.message).toBe(FALLBACK)
    expect(byCode.kind).toBe('unknown')
    expect(byCode.message).toBe(FALLBACK)
    expect(notApiError.kind).toBe('unknown')
    expect(notApiError.message).toBe(FALLBACK)
  })

  it('английский detail никогда не показывается', () => {
    const failure = describePasswordResetFailure(
      new ApiError(
        400,
        {
          detail: 'Server diagnostic text.',
          errorCodes: ['auth.passwordReset.invalid'],
        },
        'Server diagnostic text.'
      ),
      FALLBACK
    )

    expect(failure.message).not.toContain('diagnostic')
    expect(failure.message).toBe('Ссылка недействительна или устарела.')
  })
})
