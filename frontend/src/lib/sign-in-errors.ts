import { ApiError } from '@/lib/api'

/**
 * Переводит ProblemDetails от API в понятный текст для формы входа.
 *
 * Коды берутся из `LoginUserHandler` и `ValidateAntiforgeryHeaderAttribute`: разбирать
 * английский `detail` нельзя — он не является контрактом. Порядок проверок важен:
 * лимит (`429`) раньше кодов, `401` — после них (AGENT.md §9.5).
 */
export function describeSignInError(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'Не удалось войти. Попробуйте ещё раз.'
  }

  if (error.status === 0) {
    return 'Нет связи с сервером. Проверьте подключение и повторите попытку.'
  }

  if (error.status === 429) {
    return 'Слишком много попыток входа. Подождите минуту и повторите.'
  }

  if (error.hasCode('auth.login.lockedOut')) {
    return 'Слишком много неудачных попыток. Вход временно заблокирован — попробуйте позже.'
  }

  if (error.hasCode('auth.login.inactive')) {
    return 'Аккаунт деактивирован. Восстановить доступ поможет поддержка.'
  }

  if (error.hasCode('auth.csrf.invalid')) {
    return 'Сессия устарела. Закройте окно и попробуйте снова.'
  }

  if (error.hasCode('auth.login.invalidCredentials') || error.status === 401) {
    return 'Неверный email или пароль.'
  }

  return 'Не удалось войти. Попробуйте ещё раз.'
}
