/**
 * Адреса кабинета барбера.
 *
 * Отдельный модуль по образцу `account-path.ts`: шапка и страница должны одинаково
 * понимать, что считается кабинетом, а не сравнивать строки в JSX. У кабинета
 * четыре раздела — записи клиентов, расписание, салоны и профиль — и отдельные
 * адреса онбординга (профиль создаётся до того, как кабинет станет доступен),
 * переноса записей (шаг из списка записей) и уведомлений (утилита, а не пункт
 * навигации).
 */

export const BARBER_CABINET_PATH = '/barber'
export const BARBER_SCHEDULE_PATH = '/barber/schedule'
export const BARBER_SALONS_PATH = '/barber/salons'
export const BARBER_PROFILE_PATH = '/barber/profile'
export const BARBER_ONBOARDING_PATH = '/barber/onboarding'
export const BARBER_IMPORT_PATH = '/barber/import'
export const BARBER_NOTIFICATIONS_PATH = '/barber/notifications'

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

/**
 * Ровно `/barber/profile` — карточка, прайс и специализации барбера.
 *
 * Отдельный предикат, как у расписания и салонов: `App.tsx` проверяет его до
 * `isBarberCabinetPath`, а общий предикат кабинета по-прежнему знает только
 * записи и расписание.
 */
export function isBarberProfilePath(pathname: string): boolean {
  return pathname === BARBER_PROFILE_PATH
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

/**
 * Ровно `/barber/notifications` — инбокс напоминаний.
 *
 * Не раздел навигации: вход — колокольчик в заголовке кабинета (§8.1). Проверка стоит
 * рядом с расписанием и салонами, до общего `isBarberCabinetPath`, чтобы вложенные
 * пути не открывали кабинет записей.
 */
export function isBarberNotificationsPath(pathname: string): boolean {
  return pathname === BARBER_NOTIFICATIONS_PATH
}
