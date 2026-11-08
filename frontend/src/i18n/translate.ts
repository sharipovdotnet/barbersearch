/**
 * Перевод строк интерфейса.
 *
 * Ключ — русский исходный текст: каталог английского языка хранит пары
 * «русская строка → английская». Это gettext-подход: новая строка сразу работает
 * по-русски, даже если перевод ещё не добавлен, а полноту каталога проверяет
 * `npm run check:i18n`, а не падение в рантайме.
 *
 * `t()` читает текущий язык из модульной переменной: её выставляет `I18nProvider`.
 * Этого достаточно, потому что `App` подписан на контекст и перерисовывает дерево
 * целиком при смене языка; компонентам не нужно тащить хук ради каждой подписи.
 */

import { DEFAULT_LOCALE, type Locale } from './locale'
import { en } from './messages/en'
import { pluralMessage, type PluralKey } from './plurals'

export type MessageParams = Record<string, string | number>

let currentLocale: Locale = DEFAULT_LOCALE

export function getLocale(): Locale {
  return currentLocale
}

export function setCurrentLocale(locale: Locale): void {
  currentLocale = locale
}

/** Подставляет `{name}`; неизвестный параметр остаётся видимым, а не исчезает. */
export function formatMessage(
  template: string,
  params?: MessageParams
): string {
  if (!params) {
    return template
  }

  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    const value = params[name]

    return value === undefined ? match : String(value)
  })
}

/** Перевод строки для явно заданного языка — нужен форматтерам и тестам. */
export function translate(
  locale: Locale,
  source: string,
  params?: MessageParams
): string {
  // Необязательный `context` разводит один русский текст с разным смыслом
  // («Подтвердить» профиль → Verify, бронь → Confirm). Ключ с контекстом имеет
  // вид `context\u0004source`; без него работает обычная пара.
  const context =
    typeof params?.context === 'string' ? params.context : null
  const key = context ? `${context}\u0004${source}` : source
  const message =
    locale === 'en' ? en[key] ?? en[source] ?? source : source

  return formatMessage(message, params)
}

/** Перевод для текущего языка интерфейса. */
export function t(source: string, params?: MessageParams): string {
  return translate(currentLocale, source, params)
}

/** Счётчик с правильной формой для текущего языка. */
export function tp(
  key: PluralKey,
  count: number,
  params?: MessageParams
): string {
  return formatMessage(pluralMessage(currentLocale, key, count), {
    ...params,
    count,
  })
}
