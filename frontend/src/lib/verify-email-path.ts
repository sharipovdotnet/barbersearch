/**
 * Адрес подтверждения электронной почты.
 *
 * Отдельный модуль по образцу `register-path.ts`: роутер самодельный, поэтому
 * `App` и страница должны сравнивать один и тот же путь, а не две строки в JSX.
 */

export const VERIFY_EMAIL_PATH = '/verify-email'

/** Ровно `/verify-email`; вложенные пути остаются витрине, как прочий неизвестный адрес. */
export function isVerifyEmailPath(pathname: string): boolean {
  return pathname === VERIFY_EMAIL_PATH
}
