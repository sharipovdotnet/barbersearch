/**
 * Разбор адресов раздела администрирования.
 *
 * Отдельный чистый модуль, потому что роутер самодельный: страница получает уже
 * разобранный маршрут, а не занимается строками внутри JSX.
 */

export type AdminTab = 'pending' | 'published'

/** Вкладка каталога: какой справочник показан. Живёт в `?tab=`, по умолчанию услуги. */
export type CatalogueTab = 'services' | 'specializations' | 'salons'

/**
 * Фильтр списка барберов: какой профиль показан. Живёт в `?status=`,
 * по умолчанию очередь проверки.
 */
export type AdminBarberStatusFilter = 'all' | 'pending' | 'hidden'

export type AdminRoute =
  | { kind: 'summary' }
  | { kind: 'reviews'; tab: AdminTab; reviewId: string | null }
  | {
      kind: 'barbers'
      barberId: string | null
      status: AdminBarberStatusFilter
    }
  | { kind: 'users'; userId: string | null }
  | { kind: 'catalogue'; tab: CatalogueTab }
  | { kind: 'bookings'; bookingId: string | null }
  | { kind: 'audit' }
  | { kind: 'notFound' }

const REVIEW_PATH = /^\/admin\/reviews\/([^/]+)$/
const BARBER_PATH = /^\/admin\/barbers\/([^/]+)$/
const USER_PATH = /^\/admin\/users\/([^/]+)$/
const BOOKING_PATH = /^\/admin\/bookings\/([^/]+)$/

const CATALOGUE_TABS: readonly CatalogueTab[] = [
  'services',
  'specializations',
  'salons',
]

const BARBER_STATUSES: readonly AdminBarberStatusFilter[] = [
  'all',
  'pending',
  'hidden',
]

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

  // Сводка — точный адрес: карточки и фильтров у раздела нет, поэтому нет
  // builder'а и вложенных путей. `/admin` по-прежнему ведёт на очередь отзывов.
  if (pathname === '/admin/summary') {
    return { kind: 'summary' }
  }

  if (pathname === '/admin/barbers') {
    return { kind: 'barbers', barberId: null, status: parseBarberStatus(search) }
  }

  if (pathname === '/admin/users') {
    return { kind: 'users', userId: null }
  }

  if (pathname === '/admin/catalogue') {
    return { kind: 'catalogue', tab: parseCatalogueTab(search) }
  }

  // Брони поддержки: список и карточка по id. Фильтры (email, статус) живут
  // в состоянии экрана и в адрес не попадают — только id брони.
  if (pathname === '/admin/bookings') {
    return { kind: 'bookings', bookingId: null }
  }

  // Журнал — точный адрес: карточки записи нет, вложенных путей у раздела тоже.
  // Builder'а нет намеренно: фильтры живут в состоянии и в ссылку не попадают.
  if (pathname === '/admin/audit') {
    return { kind: 'audit' }
  }

  const barberMatch = BARBER_PATH.exec(pathname)

  if (barberMatch?.[1]) {
    return {
      kind: 'barbers',
      barberId: barberMatch[1],
      status: parseBarberStatus(search),
    }
  }

  const userMatch = USER_PATH.exec(pathname)

  if (userMatch?.[1]) {
    return { kind: 'users', userId: userMatch[1] }
  }

  const bookingMatch = BOOKING_PATH.exec(pathname)

  if (bookingMatch?.[1]) {
    return { kind: 'bookings', bookingId: bookingMatch[1] }
  }

  const match = REVIEW_PATH.exec(pathname)

  if (match?.[1]) {
    return { kind: 'reviews', tab, reviewId: match[1] }
  }

  return { kind: 'notFound' }
}

/**
 * Разбирает `?status=` барберов: неизвестное значение — очередь проверки.
 *
 * Так же ведёт себя `?tab=` каталога и отзывов: ссылка с опечаткой не должна
 * давать «Раздел не найден», потому что раздел существует.
 */
export function parseBarberStatus(search: string): AdminBarberStatusFilter {
  const value = new URLSearchParams(search).get('status')

  return BARBER_STATUSES.find((status) => status === value) ?? 'pending'
}

/**
 * Адрес списка барберов: выбранный профиль — в пути, фильтр — в `?status=`.
 *
 * Фильтр едет в адресе вместе с карточкой, иначе перезагрузка или ссылка на
 * профиль открывала бы карточку в чужой выборке. Значение по умолчанию
 * (очередь проверки) в ссылку не попадает.
 */
export function barbersPath(
  barberId: string | null = null,
  status: AdminBarberStatusFilter = 'pending'
): string {
  const base = barberId
    ? `/admin/barbers/${encodeURIComponent(barberId)}`
    : '/admin/barbers'

  return status === 'pending' ? base : `${base}?status=${status}`
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

/** Адрес броней: выбранная карточка — в пути, фильтры остаются в состоянии. */
export function bookingsPath(bookingId: string | null = null): string {
  return bookingId
    ? `/admin/bookings/${encodeURIComponent(bookingId)}`
    : '/admin/bookings'
}

/** Идентификатор брони должен быть GUID: иначе запрос заведомо не найдёт запись. */
export function isBookingId(value: string): boolean {
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

/**
 * Разбирает `?tab=` каталога: неизвестная вкладка — услуги.
 *
 * Так же ведёт себя `?tab=` отзывов: ссылка с опечаткой не должна давать «Раздел не найден»,
 * потому что раздел существует.
 */
export function parseCatalogueTab(search: string): CatalogueTab {
  const value = new URLSearchParams(search).get('tab')

  return CATALOGUE_TABS.find((tab) => tab === value) ?? 'services'
}

/**
 * Адрес каталога: вкладка — в query, по умолчанию услуги.
 *
 * Карточки строки нет: список несёт все поля формы, отдельного чтения по id не существует.
 */
export function cataloguePath(tab: CatalogueTab = 'services'): string {
  return tab === 'services'
    ? '/admin/catalogue'
    : `/admin/catalogue?tab=${tab}`
}

/** Идентификатор отзыва должен быть GUID: иначе запрос заведомо не найдёт запись. */
export function isReviewId(value: string): boolean {
  return GUID_PATTERN.test(value)
}
