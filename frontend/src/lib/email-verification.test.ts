import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  confirmEmailVerification,
  describeEmailVerificationFailure,
  requestEmailVerification,
} from '@/lib/email-verification'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const FALLBACK = 'Не удалось подтвердить адрес. Попробуйте ещё раз.'

describe('email-verification: транспорт', () => {
  it('запрос письма идёт через CSRF и без тела', async () => {
    let header: string | null = null
    let body = 'unset'

    server.use(
      http.post('/api/auth/email-verification', async ({ request }) => {
        header = request.headers.get('x-csrf-token')
        body = await request.text()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await requestEmailVerification()

    expect(header).toBe('csrf-token')
    expect(body).toBe('')
  })

  it('подтверждение отправляет токен без CSRF-запроса и заголовка', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let header: string | null = 'unset'
    let payload: unknown = null

    server.use(
      http.post('/api/auth/email-verification/confirm', async ({ request }) => {
        header = request.headers.get('x-csrf-token')
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await confirmEmailVerification('token-123')

    expect(header).toBeNull()
    expect(payload).toEqual({ token: 'token-123' })
    expect(csrfCalls()).toBe(0)
  })
})

describe('email-verification: коды ошибок', () => {
  it('кулдаун', () => {
    const failure = describeEmailVerificationFailure(
      new ApiError(
        409,
        { errorCodes: ['auth.emailVerification.cooldown'] },
        'Conflict'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('cooldown')
    expect(failure.message).toBe(
      'Письмо уже отправлено. Повторите через минуту.'
    )
  })

  it('почта не настроена', () => {
    const failure = describeEmailVerificationFailure(
      new ApiError(
        409,
        { errorCodes: ['auth.emailVerification.unavailable'] },
        'Conflict'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('unavailable')
    expect(failure.message).toBe('Отправка писем пока не настроена.')
  })

  it('сбой отправки по коду и по 5xx', () => {
    const byCode = describeEmailVerificationFailure(
      new ApiError(
        500,
        { errorCodes: ['auth.emailVerification.sendFailed'] },
        'Failure'
      ),
      FALLBACK
    )
    const byStatus = describeEmailVerificationFailure(
      new ApiError(503, null, 'Failure'),
      FALLBACK
    )

    expect(byCode.kind).toBe('sendFailed')
    expect(byCode.message).toBe(
      'Не удалось отправить письмо. Попробуйте позже.'
    )
    expect(byStatus.kind).toBe('sendFailed')
    expect(byStatus.message).toBe(
      'Не удалось отправить письмо. Попробуйте позже.'
    )
  })

  it('недействительная ссылка', () => {
    const failure = describeEmailVerificationFailure(
      new ApiError(
        400,
        { errorCodes: ['auth.emailVerification.invalid'] },
        'Bad Request'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('invalid')
    expect(failure.message).toBe('Ссылка недействительна или устарела.')
  })

  it('сеть', () => {
    const failure = describeEmailVerificationFailure(
      new ApiError(0, null, 'Network'),
      FALLBACK
    )

    expect(failure.kind).toBe('connection')
    expect(failure.message).toBe(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
  })

  it('неизвестная ошибка получает запасной текст', () => {
    const failure = describeEmailVerificationFailure(
      new ApiError(400, { errorCodes: ['auth.csrf.invalid'] }, 'Bad Request'),
      FALLBACK
    )

    expect(failure.kind).toBe('unknown')
    expect(failure.message).toBe(FALLBACK)
  })
})
