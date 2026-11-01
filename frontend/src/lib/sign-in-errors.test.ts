import { describe, expect, it } from 'vitest'

import { ApiError, type ProblemDetails } from '@/lib/api'
import { describeSignInError } from '@/lib/sign-in-errors'

/**
 * Тексты — часть контракта (AGENT.md §9.5), поэтому сравниваются целиком.
 * Копия может меняться: изменение текста должно ломать эти тесты.
 */
function apiError(
  status: number,
  problem: ProblemDetails | null = null,
  message = 'Request failed.'
): ApiError {
  return new ApiError(status, problem, message)
}

describe('describeSignInError', () => {
  it('на не-ApiError отдаёт общий текст — русская копия', () => {
    expect(describeSignInError(new Error('boom'))).toBe(
      'Не удалось войти. Попробуйте ещё раз.'
    )
    expect(describeSignInError('boom')).toBe(
      'Не удалось войти. Попробуйте ещё раз.'
    )
  })

  it('на status 0 отдаёт текст про связь — русская копия', () => {
    expect(describeSignInError(apiError(0))).toBe(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
  })

  it('на 429 отдаёт текст про лимит попыток — русская копия', () => {
    expect(describeSignInError(apiError(429))).toBe(
      'Слишком много попыток входа. Подождите минуту и повторите.'
    )
  })

  it('на 429 с кодом блокировки всё равно отдаёт текст про лимит: 429 раньше кодов', () => {
    expect(
      describeSignInError(
        apiError(429, { errorCodes: ['auth.login.lockedOut'] })
      )
    ).toBe('Слишком много попыток входа. Подождите минуту и повторите.')
  })

  it('на код auth.login.lockedOut отдаёт текст про блокировку — русская копия', () => {
    expect(
      describeSignInError(
        apiError(401, { errorCodes: ['auth.login.lockedOut'] })
      )
    ).toBe(
      'Слишком много неудачных попыток. Вход временно заблокирован — попробуйте позже.'
    )
  })

  it('на код auth.login.inactive отдаёт текст про деактивацию — русская копия', () => {
    expect(
      describeSignInError(apiError(401, { errorCodes: ['auth.login.inactive'] }))
    ).toBe(
      'Аккаунт деактивирован. Восстановить доступ поможет поддержка.'
    )
  })

  it('на код auth.csrf.invalid отдаёт текст про устаревшую сессию — русская копия', () => {
    expect(
      describeSignInError(apiError(400, { errorCodes: ['auth.csrf.invalid'] }))
    ).toBe('Сессия устарела. Закройте окно и попробуйте снова.')
  })

  it('на код auth.login.invalidCredentials отдаёт текст про неверные данные — русская копия', () => {
    expect(
      describeSignInError(
        apiError(400, { errorCodes: ['auth.login.invalidCredentials'] })
      )
    ).toBe('Неверный email или пароль.')
  })

  it('на 401 без кода отдаёт текст про неверные данные: 401 после кодов', () => {
    expect(describeSignInError(apiError(401))).toBe(
      'Неверный email или пароль.'
    )
  })

  it('на 401 с кодом auth.csrf.invalid отдаёт текст про сессию, а не про пароль', () => {
    expect(
      describeSignInError(apiError(401, { errorCodes: ['auth.csrf.invalid'] }))
    ).toBe('Сессия устарела. Закройте окно и попробуйте снова.')
  })

  it('на 401 с неизвестным кодом отдаёт текст про неверные данные', () => {
    expect(
      describeSignInError(apiError(401, { errorCodes: ['auth.login.unknown'] }))
    ).toBe('Неверный email или пароль.')
  })

  it('на ApiError без кода и не про вход отдаёт общий текст', () => {
    expect(describeSignInError(apiError(500))).toBe(
      'Не удалось войти. Попробуйте ещё раз.'
    )
    expect(
      describeSignInError(apiError(403, { errorCodes: ['auth.forbidden'] }))
    ).toBe('Не удалось войти. Попробуйте ещё раз.')
  })

  it('не использует английский detail сервера', () => {
    expect(
      describeSignInError(
        apiError(500, {
          detail: 'An unexpected error occurred',
          errorCodes: [],
        })
      )
    ).toBe('Не удалось войти. Попробуйте ещё раз.')
  })
})
