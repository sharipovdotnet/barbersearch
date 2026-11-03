/**
 * Адреса кабинета барбера.
 *
 * Отдельный модуль по образцу `account-path.ts`: шапка и страница должны одинаково
 * понимать, что считается кабинетом, а не сравнивать строки в JSX. У кабинета два
 * раздела — записи клиентов и расписание; неизвестные вложенные пути остаются витрине.
 */

export const BARBER_CABINET_PATH = '/barber'
export const BARBER_SCHEDULE_PATH = '/barber/schedule'

/** Ровно `/barber` или `/barber/schedule`; прочие вложенные пути не открывают кабинет. */
export function isBarberCabinetPath(pathname: string): boolean {
  return pathname === BARBER_CABINET_PATH || pathname === BARBER_SCHEDULE_PATH
}

/** Ровно `/barber/schedule`. */
export function isBarberSchedulePath(pathname: string): boolean {
  return pathname === BARBER_SCHEDULE_PATH
}
