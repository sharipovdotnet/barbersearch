/**
 * Адреса личного кабинета.
 *
 * Отдельный модуль, потому что роутер самодельный: шапке и страницам нужен один
 * источник истины о том, что считается кабинетом, а не сравнение строк в JSX.
 * Кабинет один для всех ролей, поэтому разделы отличаются только адресом.
 */

export const ACCOUNT_PATH = '/account'
export const ACCOUNT_REVIEWS_PATH = '/account/reviews'
export const ACCOUNT_PROFILE_PATH = '/account/profile'
export const ACCOUNT_SECURITY_PATH = '/account/security'

export type AccountSection = 'bookings' | 'reviews' | 'profile' | 'security'

/** Раздел кабинета по адресу; `null` — это не кабинет. */
export function accountSection(pathname: string): AccountSection | null {
  switch (pathname) {
    case ACCOUNT_PATH:
      return 'bookings'
    case ACCOUNT_REVIEWS_PATH:
      return 'reviews'
    case ACCOUNT_PROFILE_PATH:
      return 'profile'
    case ACCOUNT_SECURITY_PATH:
      return 'security'
    default:
      return null
  }
}

/** Любой экран кабинета: записи, отзывы, профиль или безопасность. */
export function isAccountPath(pathname: string): boolean {
  return accountSection(pathname) !== null
}

/** Отдельный экран отзывов; проверяется до `/account`. */
export function isAccountReviewsPath(pathname: string): boolean {
  return pathname === ACCOUNT_REVIEWS_PATH
}

/** Профиль; проверяется до `/account`. */
export function isAccountProfilePath(pathname: string): boolean {
  return pathname === ACCOUNT_PROFILE_PATH
}

/** Безопасность; проверяется до `/account`. */
export function isAccountSecurityPath(pathname: string): boolean {
  return pathname === ACCOUNT_SECURITY_PATH
}
