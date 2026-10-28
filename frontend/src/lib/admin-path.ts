/**
 * Разбор адресов раздела администрирования.
 *
 * Отдельный чистый модуль, потому что роутер самодельный: страница получает уже
 * разобранный маршрут, а не занимается строками внутри JSX.
 */

export type AdminTab = 'pending' | 'published'

export type AdminRoute =
  | { kind: 'reviews'; tab: AdminTab; reviewId: string | null }
  | { kind: 'notFound' }

const REVIEW_PATH = /^\/admin\/reviews\/([^/]+)$/

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

  const match = REVIEW_PATH.exec(pathname)

  if (match?.[1]) {
    return { kind: 'reviews', tab, reviewId: match[1] }
  }

  return { kind: 'notFound' }
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
