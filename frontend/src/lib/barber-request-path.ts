/**
 * Адрес страницы «Стать барбером».
 *
 * Отдельный модуль по образцу `register-path.ts`: на путь ссылаются карточка
 * в кабинете, сама страница и `App.tsx`, а роутер самодельный — сравнение строк
 * в JSX разошлось бы. Ссылки в шапке нет: страница открывается из кабинета
 * и по прямому адресу.
 */

export const BECOME_A_BARBER_PATH = '/become-a-barber'

/**
 * Ровно `/become-a-barber`; вложенные пути остаются витрине, как прочий неизвестный
 * адрес. `App.tsx` проверяет предикат **до** `matchBarberPath`.
 */
export function isBecomeABarberPath(pathname: string): boolean {
  return pathname === BECOME_A_BARBER_PATH
}
