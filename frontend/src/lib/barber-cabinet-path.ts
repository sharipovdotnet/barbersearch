/**
 * Адрес кабинета барбера.
 *
 * Отдельный модуль по образцу `account-path.ts`: шапка и страница должны одинаково
 * понимать, что считается кабинетом, а не сравнивать строки в JSX. Вложенных адресов
 * у кабинета нет, поэтому `/barber/*` остаётся витрине.
 */

export const BARBER_CABINET_PATH = '/barber'

/** Ровно `/barber`; неизвестные вложенные пути не открывают кабинет. */
export function isBarberCabinetPath(pathname: string): boolean {
  return pathname === BARBER_CABINET_PATH
}
