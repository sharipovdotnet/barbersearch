import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'

/**
 * Восстановление забытого пароля по email.
 *
 * Оба запроса анонимны и идут **без CSRF**: у `request` нет ambient-идентичности
 * (email приходит в теле, а не из cookie), а `confirm` авторизуется одноразовым
 * токеном из письма, которое могут открыть в другом браузере. Поэтому здесь нет
 * `mutateWithCsrf` и запроса `GET /api/auth/csrf` — как у `register` и
 * `email-verification/confirm` (§9.4 `AGENT.md`).
 *
 * `request` отвечает `204` для любого адреса: существование аккаунта по ответу
 * не определить. Экран обязан повторять это и никогда не различать случаи.
 */

export const PASSWORD_RESET_ERROR_CODES = {
  invalid: 'auth.passwordReset.invalid',
  unavailable: 'auth.passwordReset.unavailable',
} as const

export type PasswordResetFailureKind =
  | 'invalid'
  | 'unavailable'
  | 'rateLimited'
  | 'connection'
  | 'unknown'

export type PasswordResetFailure = {
  kind: PasswordResetFailureKind
  message: string
}

/**
 * Публичный код (или класс сбоя) → русский текст.
 *
 * Английский `detail` не показываем никогда — он диагностический, а не контракт.
 * Коды политики пароля (`auth.password.*`) сюда не входят: они относятся к полю
 * новой формы, а не к ссылке, поэтому разбираются на странице сброса.
 */
export function describePasswordResetFailure(
  error: unknown,
  fallback: string
): PasswordResetFailure {
  if (!(error instanceof ApiError)) {
    return { kind: 'unknown', message: t(fallback) }
  }

  if (error.status === 0) {
    return {
      kind: 'connection',
      message: t(
        'Нет связи с сервером. Проверьте подключение и повторите попытку.'
      ),
    }
  }

  if (error.hasCode(PASSWORD_RESET_ERROR_CODES.invalid)) {
    return {
      kind: 'invalid',
      message: t('Ссылка недействительна или устарела.'),
    }
  }

  if (error.hasCode(PASSWORD_RESET_ERROR_CODES.unavailable)) {
    return {
      kind: 'unavailable',
      message: t('Сброс пароля по почте пока не настроен.'),
    }
  }

  if (error.status === 429) {
    return {
      kind: 'rateLimited',
      message: t('Слишком много попыток. Подождите минуту и повторите.'),
    }
  }

  return { kind: 'unknown', message: t(fallback) }
}

/**
 * Попросить ссылку для сброса пароля.
 *
 * `204` означает «письмо отправлено, если аккаунт существует»; `409` — глобальный
 * отказ (почта не настроена), `429` — лимитер. Остальные ошибки экран показывает
 * запасным текстом.
 */
export async function requestPasswordReset(
  email: string,
  signal?: AbortSignal
): Promise<void> {
  await apiRequest<void>('/auth/password-reset', {
    method: 'POST',
    body: JSON.stringify({ email }),
    signal,
  })
}

/**
 * Задать новый пароль по одноразовому токену из письма.
 *
 * Повторный клик по той же ссылке — `400 auth.passwordReset.invalid`: confirm не
 * идемпотентен, и второй запрос не должен «доигрывать» первый (§3.7).
 */
export async function confirmPasswordReset(
  token: string,
  newPassword: string,
  signal?: AbortSignal
): Promise<void> {
  await apiRequest<void>('/auth/password-reset/confirm', {
    method: 'POST',
    body: JSON.stringify({ token, newPassword }),
    signal,
  })
}
