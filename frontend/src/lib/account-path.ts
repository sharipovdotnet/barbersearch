/**
 * Адреса кабинета клиента.
 *
 * Отдельный модуль, потому что роутер самодельный: шапке и страницам нужен один
 * источник истины о том, что считается кабинетом, а не сравнение строк в JSX.
 */

export const ACCOUNT_PATH = '/account'
export const ACCOUNT_REVIEWS_PATH = '/account/reviews'

/** Кабинет — и «Мои записи», и «Мои отзывы». */
export function isAccountPath(pathname: string): boolean {
  return pathname === ACCOUNT_PATH || pathname === ACCOUNT_REVIEWS_PATH
}

/** Отдельный экран отзывов; проверяется до `/account`. */
export function isAccountReviewsPath(pathname: string): boolean {
  return pathname === ACCOUNT_REVIEWS_PATH
}
