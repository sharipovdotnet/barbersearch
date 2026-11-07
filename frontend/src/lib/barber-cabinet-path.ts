/**
 * Адреса кабинета барбера.
 *
 * Отдельный модуль по образцу `account-path.ts`: шапка и страница должны одинаково
 * понимать, что считается кабинетом, а не сравнивать строки в JSX. У кабинета три
 * раздела — записи клиентов, расписание и салоны — и отдельные адреса онбординга
 * (профиль создаётся до того, как кабинет станет доступен) и переноса записей
 * (шаг из списка записей, а не четвёртый раздел навигации).
 */

export const BARBER_CABINET_PATH = '/barber'
export const BARBER_SCHEDULE_PATH = '/barber/schedule'
export const BARBER_SALONS_PATH = '/barber/salons'
export const BARBER_ONBOARDING_PATH = '/barber/onboarding'
export const BARBER_IMPORT_PATH = '/barber/import'

/**
 * Ровно `/barber` или `/barber/schedule`; прочие вложенные пути не открывают кабинет.
 *
 * Салоны сюда не входят: `App.tsx` проверяет `isBarberSalonsPath` отдельным предикатом
 * и рендерит свой экран до `isBarberCabinetPath` — так же, как онбординг.
 */
export function isBarberCabinetPath(pathname: string): boolean {
  return pathname === BARBER_CABINET_PATH || pathname === BARBER_SCHEDULE_PATH
}

/** Ровно `/barber/schedule`. */
export function isBarberSchedulePath(pathname: string): boolean {
  return pathname === BARBER_SCHEDULE_PATH
}

/** Ровно `/barber/salons`. */
export function isBarberSalonsPath(pathname: string): boolean {
  return pathname === BARBER_SALONS_PATH
}

/** Ровно `/barber/onboarding`. */
export function isBarberOnboardingPath(pathname: string): boolean {
  return pathname === BARBER_ONBOARDING_PATH
}

/**
 * Ровно `/barber/import` — перенос старых записей.
 *
 * Отдельный предикат, как у расписания и салонов: кабинет в `isBarberCabinetPath`
 * не входит, поэтому проверка стоит рядом и рендерит свой экран.
 */
export function isBarberImportPath(pathname: string): boolean {
  return pathname === BARBER_IMPORT_PATH
}
