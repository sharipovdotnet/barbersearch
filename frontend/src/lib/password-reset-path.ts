/**
 * Адреса восстановления забытого пароля.
 *
 * Отдельный модуль по образцу `register-path.ts`: роутер самодельный, поэтому
 * `App`, страницы и ссылки должны сравнивать один и тот же путь, а не две строки
 * в JSX.
 */

export const FORGOT_PASSWORD_PATH = '/forgot-password'
export const RESET_PASSWORD_PATH = '/reset-password'

/** Ровно `/forgot-password`; вложенные пути остаются витрине, как прочий неизвестный адрес. */
export function isForgotPasswordPath(pathname: string): boolean {
  return pathname === FORGOT_PASSWORD_PATH
}

/** Ровно `/reset-password`; query с токеном проверяется отдельно на странице. */
export function isResetPasswordPath(pathname: string): boolean {
  return pathname === RESET_PASSWORD_PATH
}
