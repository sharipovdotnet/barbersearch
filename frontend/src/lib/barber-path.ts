/**
 * Разбор публичного адреса барбера `/barbers/{id}`.
 *
 * Отдельный чистый модуль по образцу `admin-path.ts`: роутер самодельный, и страница
 * получает уже разобранный идентификатор, а не занимается строками в JSX.
 */

const BARBER_PATH = /^\/barbers\/([^/]+)$/

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/**
 * Идентификатор из пути либо `null`, если путь не является карточкой барбера.
 * `/barbers` без id и `/barbers/foo/bar` сюда не подходят и остаются витрине.
 */
export function matchBarberPath(pathname: string): string | null {
  const match = BARBER_PATH.exec(pathname)

  if (!match?.[1]) {
    return null
  }

  try {
    return decodeURIComponent(match[1])
  } catch {
    // Битая процентная кодировка не должна валить рендер: id просто не станет GUID.
    return match[1]
  }
}

/** Адрес карточки барбера. */
export function barberPath(id: string): string {
  return `/barbers/${encodeURIComponent(id)}`
}

/** Идентификатор барбера должен быть GUID: иначе запрос заведомо не найдёт профиль. */
export function isBarberId(value: string): boolean {
  return GUID_PATTERN.test(value)
}
