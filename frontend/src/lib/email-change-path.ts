/**
 * Адрес подтверждения смены электронной почты.
 *
 * Отдельный модуль по образцу `verify-email-path.ts`: роутер самодельный, поэтому
 * `App` и страница должны сравнивать один и тот же путь, а не две строки в JSX.
 */

export const CONFIRM_EMAIL_CHANGE_PATH = '/confirm-email-change'

/**
 * Ровно `/confirm-email-change`; вложенные пути остаются витрине, как прочий
 * неизвестный адрес.
 */
export function isConfirmEmailChangePath(pathname: string): boolean {
  return pathname === CONFIRM_EMAIL_CHANGE_PATH
}
