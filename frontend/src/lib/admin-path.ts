/**
 * Разбор адресов раздела администрирования.
 *
 * Отдельный чистый модуль, потому что роутер самодельный: страница получает уже
 * разобранный маршрут, а не занимается строками внутри JSX.
 */

export type AdminTab = 'pending' | 'published'

export type AdminRoute =
  | { kind: 'reviews'; tab: AdminTab; reviewId: string | null }
  | { kind: 'barbers'; barberId: string | null }
  | { kind: 'users'; userId: string | null }
  | { kind: 'notFound' }

const REVIEW_PATH = /^\/admin\/reviews\/([^/]+)$/
const BARBER_PATH = /^\/admin\/barbers\/([^/]+)$/
const USER_PATH = /^\/admin\/users\/([^/]+)$/

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Раздел администрирования — всё, что начинается с `/admin`. */
export function isAdminPath(pathname: string): boolean {
  return pathname === '/admin' || pathname.startsWith('/admin/')
}

/** Адрес раздела или `notFound` для неизвестного вложенного пути. */
export function parseAdminPath(pathname: string, search: string): AdminRoute {
  if (!isAdminPath(pathname)) {
    return { kind: 'notFound' }
  }

  const tab: AdminTab =
    new URLSearchParams(search).get('tab') === 'published'
      ? 'published'
      : 'pending'

  if (pathname === '/admin' || pathname === '/admin/reviews') {
    return { kind: 'reviews', tab, reviewId: null }
  }

  if (pathname === '/admin/barbers') {
    return { kind: 'barbers', barberId: null }
  }

  if (pathname === '/admin/users') {
    return { kind: 'users', userId: null }
  }

  const barberMatch = BARBER_PATH.exec(pathname)

  if (barberMatch?.[1]) {
    return { kind: 'barbers', barberId: barberMatch[1] }
  }

  const userMatch = USER_PATH.exec(pathname)

  if (userMatch?.[1]) {
    return { kind: 'users', userId: userMatch[1] }
  }

  const match = REVIEW_PATH.exec(pathname)

  if (match?.[1]) {
    return { kind: 'reviews', tab, reviewId: match[1] }
  }

  return { kind: 'notFound' }
}

/** Адрес списка барберов: выбранный профиль — в пути. */
export function barbersPath(barberId: string | null = null): string {
  return barberId
    ? `/admin/barbers/${encodeURIComponent(barberId)}`
    : '/admin/barbers'
}

/** Идентификатор барбера должен быть GUID: иначе запрос заведомо не найдёт запись. */
export function isBarberId(value: string): boolean {
  return GUID_PATTERN.test(value)
}

/** Адрес списка пользователей: выбранный аккаунт — в пути. */
export function usersPath(userId: string | null = null): string {
  return userId
    ? `/admin/users/${encodeURIComponent(userId)}`
    : '/admin/users'
}

/** Идентификатор пользователя должен быть GUID: иначе запрос заведомо не найдёт запись. */
export function isUserId(value: string): boolean {
  return GUID_PATTERN.test(value)
}

/** Адрес списка отзывов: вкладка в query, выбранный отзыв — в пути. */
export function reviewsPath(
  tab: AdminTab,
  reviewId: string | null = null
): string {
  const base = reviewId
    ? `/admin/reviews/${encodeURIComponent(reviewId)}`
    : '/admin/reviews'

  return tab === 'published' ? `${base}?tab=published` : base
}

/** Идентификатор отзыва должен быть GUID: иначе запрос заведомо не найдёт запись. */
export function isReviewId(value: string): boolean {
  return GUID_PATTERN.test(value)
}
