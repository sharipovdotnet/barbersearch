/**
 * Адреса кабинета барбера.
 *
 * Отдельный модуль по образцу `account-path.ts`: шапка и страница должны одинаково
 * понимать, что считается кабинетом, а не сравнивать строки в JSX. У кабинета три
 * раздела — записи клиентов, расписание и салоны — и отдельный адрес онбординга:
 * профиль создаётся до того, как кабинет станет доступен, поэтому у него свой гейт.
 */

export const BARBER_CABINET_PATH = '/barber'
export const BARBER_SCHEDULE_PATH = '/barber/schedule'
export const BARBER_SALONS_PATH = '/barber/salons'
export const BARBER_ONBOARDING_PATH = '/barber/onboarding'

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
