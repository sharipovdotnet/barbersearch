/**
 * Адрес самостоятельной регистрации.
 *
 * Отдельный модуль, потому что вход и гейты ссылаются на один и тот же путь, а роутер
 * самодельный: сравнение строк в JSX разошлось бы между ними.
 */

export const REGISTER_PATH = '/register'

/** Ровно `/register`; вложенные пути остаются витрине, как и прочий неизвестный адрес. */
export function isRegisterPath(pathname: string): boolean {
  return pathname === REGISTER_PATH
}
