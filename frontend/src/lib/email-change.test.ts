import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  confirmEmailChange,
  describeEmailChangeFailure,
  requestEmailChange,
} from '@/lib/email-change'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const FALLBACK = 'Не удалось изменить email. Попробуйте позже.'

describe('email-change: транспорт', () => {
  it('запрос смены идёт через CSRF и отправляет адрес с текущим паролем', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let header: string | null = null
    let payload: unknown = null

    server.use(
      http.post('/api/auth/email-change', async ({ request }) => {
        header = request.headers.get('x-csrf-token')
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await requestEmailChange('new@example.com', 'current-secret')

    expect(header).toBe('csrf-token')
    expect(payload).toEqual({
      newEmail: 'new@example.com',
      currentPassword: 'current-secret',
    })
    expect(csrfCalls()).toBe(1)
  })

  it('подтверждение отправляет токен без CSRF-запроса и заголовка', async () => {
    const csrfCalls = countRequests('/api/auth/csrf')
    let header: string | null = 'unset'
    let payload: unknown = null

    server.use(
      http.post('/api/auth/email-change/confirm', async ({ request }) => {
        header = request.headers.get('x-csrf-token')
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    await confirmEmailChange('token-123')

    expect(header).toBeNull()
    expect(payload).toEqual({ token: 'token-123' })
    expect(csrfCalls()).toBe(0)
  })
})

describe('email-change: коды ошибок', () => {
  it('пустой или кривой адрес становится ошибкой поля newEmail', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(
        400,
        { errorCodes: ['user.changeEmail.newEmail'] },
        'Invalid new email.'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('field')
    expect(failure.field).toBe('newEmail')
    expect(failure.message).toBe(
      'Проверьте email: нужен адрес вида name@example.com.'
    )
  })

  it('совпадение с текущим адресом — тоже поле newEmail', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(
        400,
        { errorCodes: ['user.changeEmail.sameEmail'] },
        'Same email.'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('field')
    expect(failure.field).toBe('newEmail')
    expect(failure.message).toBe('Этот адрес уже указан в профиле.')
  })

  it('неверный текущий пароль — поле currentPassword', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(
        400,
        { errorCodes: ['auth.emailChange.invalidCurrentPassword'] },
        'Invalid current password.'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('field')
    expect(failure.field).toBe('currentPassword')
    expect(failure.message).toBe('Текущий пароль неверный.')
  })

  it('занятый адрес показывается у поля newEmail, хотя это 409', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(
        409,
        { errorCodes: ['user.changeEmail.emailTaken'] },
        'Conflict'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('emailTaken')
    expect(failure.field).toBe('newEmail')
    expect(failure.message).toBe('Этот адрес уже занят. Укажите другой.')
  })

  it('кулдаун и недоступность почты остаются ошибками формы', () => {
    const cooldown = describeEmailChangeFailure(
      new ApiError(
        409,
        { errorCodes: ['auth.emailChange.cooldown'] },
        'Conflict'
      ),
      FALLBACK
    )
    const unavailable = describeEmailChangeFailure(
      new ApiError(
        409,
        { errorCodes: ['auth.emailChange.unavailable'] },
        'Conflict'
      ),
      FALLBACK
    )

    expect(cooldown.kind).toBe('cooldown')
    expect(cooldown.field).toBeNull()
    expect(cooldown.message).toBe(
      'Письмо уже отправлено. Повторите через минуту.'
    )
    expect(unavailable.kind).toBe('unavailable')
    expect(unavailable.field).toBeNull()
    expect(unavailable.message).toBe('Отправка писем пока не настроена.')
  })

  it('битая ссылка', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(
        400,
        { errorCodes: ['auth.emailChange.invalid'] },
        'Bad Request'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('invalid')
    expect(failure.field).toBeNull()
    expect(failure.message).toBe('Ссылка недействительна или устарела.')
  })

  it('429 — лимитер, а не ошибка поля', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(429, null, 'Too Many Requests'),
      FALLBACK
    )

    expect(failure.kind).toBe('rateLimited')
    expect(failure.field).toBeNull()
    expect(failure.message).toBe(
      'Слишком много попыток. Подождите минуту и повторите.'
    )
  })

  it('сеть', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(0, null, 'Network'),
      FALLBACK
    )

    expect(failure.kind).toBe('connection')
    expect(failure.field).toBeNull()
    expect(failure.message).toBe(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
  })

  it('неизвестная ошибка получает запасной текст, а не английский detail', () => {
    const failure = describeEmailChangeFailure(
      new ApiError(
        500,
        {
          detail: 'An unexpected error occurred.',
          errorCodes: ['auth.csrf.invalid'],
        },
        'Server error'
      ),
      FALLBACK
    )

    expect(failure.kind).toBe('unknown')
    expect(failure.field).toBeNull()
    expect(failure.message).toBe(FALLBACK)
    expect(failure.message).not.toContain('unexpected')
  })
})
