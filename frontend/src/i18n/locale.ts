/**
 * Языки интерфейса и их хранение.
 *
 * Язык выбирает пользователь; по умолчанию интерфейс русский. Выбор переживает
 * перезагрузку: он лежит в `localStorage`, а не в cookie, потому что серверу он не
 * нужен, а cookie-реестр Политики описывает только строго необходимые cookie.
 */

export const LOCALES = ['ru', 'en'] as const

export type Locale = (typeof LOCALES)[number]

export const DEFAULT_LOCALE: Locale = 'ru'

export const LOCALE_STORAGE_KEY = 'barbersearch.locale'

/**
 * `en-GB`, а не `en-US`: в интерфейсе круглосуточное время и европейский порядок
 * даты, как в русской версии, а не AM/PM.
 */
export function localeTag(locale: Locale): string {
  return locale === 'ru' ? 'ru-RU' : 'en-GB'
}

/** Язык можно передать только известным значением: всё остальное — русский. */
export function isLocale(value: unknown): value is Locale {
  return value === 'ru' || value === 'en'
}

/**
 * Сохранённый выбор. `localStorage` может быть недоступен (приватный режим,
 * заблокированное хранилище), поэтому чтение и запись не должны ронять приложение.
 */
export function readStoredLocale(): Locale {
  try {
    const stored = window.localStorage.getItem(LOCALE_STORAGE_KEY)

    return isLocale(stored) ? stored : DEFAULT_LOCALE
  } catch {
    return DEFAULT_LOCALE
  }
}

export function storeLocale(locale: Locale): void {
  try {
    window.localStorage.setItem(LOCALE_STORAGE_KEY, locale)
  } catch {
    // Выбор языка остаётся в памяти до перезагрузки — это лучше, чем падение.
  }
}
