import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'

/**
 * Смена адреса электронной почты с подтверждением.
 *
 * Запрос смены — cookie-мутация вошедшего пользователя: адрес и текущий пароль
 * приходят в теле, поэтому запрос идёт через `mutateWithCsrf`. Подтверждение,
 * наоборот, анонимно и без CSRF — ссылку открывают в другом браузере, а секрет
 * заключён в самом токене (§9.4 `AGENT.md`).
 *
 * Английский `detail` не показываем никогда: публичные коды — часть контракта,
 * а `detail` диагностический (backend `FailureProblemDetails`).
 */

/** Публичные коды сервера, которые экран умеет объяснить по-русски. */
export const EMAIL_CHANGE_ERROR_CODES = {
  newEmail: 'user.changeEmail.newEmail',
  sameEmail: 'user.changeEmail.sameEmail',
  invalidCurrentPassword: 'auth.emailChange.invalidCurrentPassword',
  emailTaken: 'user.changeEmail.emailTaken',
  cooldown: 'auth.emailChange.cooldown',
  unavailable: 'auth.emailChange.unavailable',
  invalid: 'auth.emailChange.invalid',
} as const

/** Поле формы, к которому относится отказ; `null` — ошибка уровня формы. */
export type EmailChangeField = 'newEmail' | 'currentPassword'

export type EmailChangeFailureKind =
  | 'field'
  | 'emailTaken'
  | 'cooldown'
  | 'unavailable'
  | 'invalid'
  | 'rateLimited'
  | 'connection'
  | 'unknown'

export type EmailChangeFailure = {
  kind: EmailChangeFailureKind
  /** Ошибка поля, а не формы; для `emailTaken` адрес тоже относится к полю. */
  field: EmailChangeField | null
  message: string
}

/**
 * Публичный код (или класс сбоя) → русский текст и место показа.
 *
 * `user.changeEmail.newEmail` и `user.changeEmail.sameEmail` — про новый адрес,
 * `auth.emailChange.invalidCurrentPassword` — про текущий пароль, занятый адрес
 * тоже показывается у поля: пользователю нужно исправить именно его. Кулдаун,
 * недоступность почты, битая ссылка и лимитер относятся к форме целиком.
 */
export function describeEmailChangeFailure(
  error: unknown,
  fallback: string
): EmailChangeFailure {
  if (!(error instanceof ApiError)) {
    return { kind: 'unknown', field: null, message: t(fallback) }
  }

  if (error.status === 0) {
    return {
      kind: 'connection',
      field: null,
      message: t(
        'Нет связи с сервером. Проверьте подключение и повторите попытку.'
      ),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.newEmail)) {
    return {
      kind: 'field',
      field: 'newEmail',
      message: t('Проверьте email: нужен адрес вида name@example.com.'),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.sameEmail)) {
    return {
      kind: 'field',
      field: 'newEmail',
      message: t('Этот адрес уже указан в профиле.'),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.invalidCurrentPassword)) {
    return {
      kind: 'field',
      field: 'currentPassword',
      message: t('Текущий пароль неверный.'),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.emailTaken)) {
    return {
      kind: 'emailTaken',
      field: 'newEmail',
      message: t('Этот адрес уже занят. Укажите другой.'),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.cooldown)) {
    return {
      kind: 'cooldown',
      field: null,
      message: t('Письмо уже отправлено. Повторите через минуту.'),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.unavailable)) {
    return {
      kind: 'unavailable',
      field: null,
      message: t('Отправка писем пока не настроена.'),
    }
  }

  if (error.hasCode(EMAIL_CHANGE_ERROR_CODES.invalid)) {
    return {
      kind: 'invalid',
      field: null,
      message: t('Ссылка недействительна или устарела.'),
    }
  }

  if (error.status === 429) {
    return {
      kind: 'rateLimited',
      field: null,
      message: t('Слишком много попыток. Подождите минуту и повторите.'),
    }
  }

  return { kind: 'unknown', field: null, message: t(fallback) }
}

/**
 * Попросить ссылку на новый адрес.
 *
 * `204` — письма поставлены в outbox: ссылка ушла на новый адрес, уведомление
 * на старый. Остальные коды разбирает `describeEmailChangeFailure`.
 */
export async function requestEmailChange(
  newEmail: string,
  currentPassword: string,
  signal?: AbortSignal
): Promise<void> {
  await mutateWithCsrf<void>('/auth/email-change', {
    method: 'POST',
    body: JSON.stringify({ newEmail, currentPassword }),
    signal,
  })
}

/**
 * Подтвердить новый адрес по токену из письма.
 *
 * Без CSRF намеренно: у анонимного действия нет личности, к которой привязан
 * antiforgery-токен, а ссылку могут открыть в другом браузере. `401` здесь не
 * ожидается, поэтому прозрачное продление сессии не включаем.
 */
export async function confirmEmailChange(
  token: string,
  signal?: AbortSignal
): Promise<void> {
  await apiRequest<void>('/auth/email-change/confirm', {
    method: 'POST',
    body: JSON.stringify({ token }),
    signal,
  })
}
