import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'

/**
 * Подтверждение адреса электронной почты.
 *
 * Запрос письма — cookie-мутация вошедшего пользователя, поэтому идёт через
 * `mutateWithCsrf` и тело не несёт: пользователь берётся из сессии. Подтверждение,
 * наоборот, анонимно и без CSRF — ссылку открывают в другом браузере, а секрет
 * заключён в самом токене (§3, §9.4 `AGENT.md`).
 */

/** Публичные коды сервера, которые экран умеет объяснить по-русски. */
export const EMAIL_VERIFICATION_ERROR_CODES = {
  invalid: 'auth.emailVerification.invalid',
  cooldown: 'auth.emailVerification.cooldown',
  unavailable: 'auth.emailVerification.unavailable',
  sendFailed: 'auth.emailVerification.sendFailed',
} as const

export type EmailVerificationFailureKind =
  | 'invalid'
  | 'cooldown'
  | 'unavailable'
  | 'sendFailed'
  | 'connection'
  | 'unknown'

export type EmailVerificationFailure = {
  kind: EmailVerificationFailureKind
  message: string
}

/** Текст кулдауна: его же показывает `429` от rate limiter в кабинете. */
export function emailVerificationCooldownMessage(): string {
  return t('Письмо уже отправлено. Повторите через минуту.')
}

/**
 * Публичный код (или класс сбоя) → русский текст.
 *
 * Английский `detail` не показываем никогда — он диагностический, а не контракт.
 * `5xx` считается сбоем отправки: сервер уже принял запрос, но письмо не ушло.
 */
export function describeEmailVerificationFailure(
  error: unknown,
  fallback: string
): EmailVerificationFailure {
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

  if (error.hasCode(EMAIL_VERIFICATION_ERROR_CODES.invalid)) {
    return {
      kind: 'invalid',
      message: t('Ссылка недействительна или устарела.'),
    }
  }

  if (error.hasCode(EMAIL_VERIFICATION_ERROR_CODES.cooldown)) {
    return { kind: 'cooldown', message: emailVerificationCooldownMessage() }
  }

  if (error.hasCode(EMAIL_VERIFICATION_ERROR_CODES.unavailable)) {
    return {
      kind: 'unavailable',
      message: t('Отправка писем пока не настроена.'),
    }
  }

  if (
    error.hasCode(EMAIL_VERIFICATION_ERROR_CODES.sendFailed) ||
    error.status >= 500
  ) {
    return {
      kind: 'sendFailed',
      message: t('Не удалось отправить письмо. Попробуйте позже.'),
    }
  }

  return { kind: 'unknown', message: t(fallback) }
}

/** Попросить письмо со ссылкой; `204` — письмо поставлено в отправку. */
export async function requestEmailVerification(): Promise<void> {
  await mutateWithCsrf<void>('/auth/email-verification', { method: 'POST' })
}

/**
 * Подтвердить адрес по токену из ссылки.
 *
 * Без CSRF намеренно: у анонимного действия нет личности, к которой привязан
 * antiforgery-токен, а ссылку могут открыть в другом браузере. `401` здесь не
 * ожидается, поэтому прозрачное продление сессии не включаем.
 */
export async function confirmEmailVerification(
  token: string,
  signal?: AbortSignal
): Promise<void> {
  await apiRequest<void>('/auth/email-verification/confirm', {
    method: 'POST',
    body: JSON.stringify({ token }),
    signal,
  })
}
