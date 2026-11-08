/**
 * Склонения счётчиков для двух языков.
 *
 * Строки с `{count}` выбираются по правилам `Intl.PluralRules`: у русского три
 * формы (one/few/many), у английского две (one/other). Ключ здесь смысловой, а не
 * русская строка: одна и та же единица счёта встречается в разных предложениях
 * («3 отзыва», «ждёт 3 дня»), и её формы не должны дублироваться.
 */

import type { Locale } from './locale'

export type PluralKey =
  | 'reviews'
  | 'profiles'
  | 'salons'
  | 'bookings'
  | 'stuckBookings'
  | 'accounts'
  | 'barbers'
  | 'events'
  | 'gaps'
  | 'days'
  | 'experience'
  | 'visits'
  | 'minutes'
  | 'hours'
  | 'slots'
  | 'rows'
  | 'shownBarbers'
  | 'leadMinutes'

type PluralMessage = {
  one: string
  few?: string
  many?: string
  other?: string
}

const RU: Record<PluralKey, PluralMessage> = {
  reviews: {
    one: '{count} отзыв',
    few: '{count} отзыва',
    many: '{count} отзывов',
  },
  profiles: {
    one: '{count} профиль',
    few: '{count} профиля',
    many: '{count} профилей',
  },
  salons: {
    one: '{count} салон',
    few: '{count} салона',
    many: '{count} салонов',
  },
  bookings: {
    one: '{count} запись',
    few: '{count} записи',
    many: '{count} записей',
  },
  stuckBookings: {
    one: '{count} бронь',
    few: '{count} брони',
    many: '{count} броней',
  },
  accounts: {
    one: '{count} аккаунт',
    few: '{count} аккаунта',
    many: '{count} аккаунтов',
  },
  barbers: {
    one: '{count} барбер',
    few: '{count} барбера',
    many: '{count} барберов',
  },
  events: {
    one: '{count} событие',
    few: '{count} события',
    many: '{count} событий',
  },
  gaps: {
    one: '{count} окно',
    few: '{count} окна',
    many: '{count} окон',
  },
  days: {
    one: '{count} день',
    few: '{count} дня',
    many: '{count} дней',
  },
  experience: {
    one: '{count} год',
    few: '{count} года',
    many: '{count} лет',
  },
  visits: {
    one: '{count} визит',
    few: '{count} визита',
    many: '{count} визитов',
  },
  minutes: {
    one: '{count} минуту',
    few: '{count} минуты',
    many: '{count} минут',
  },
  hours: {
    one: '{count} час',
    few: '{count} часа',
    many: '{count} часов',
  },
  slots: {
    one: '{count} слот',
    few: '{count} слота',
    many: '{count} слотов',
  },
  rows: {
    one: '{count} строка',
    few: '{count} строки',
    many: '{count} строк',
  },
  shownBarbers: {
    one: 'Показан {count} барбер',
    few: 'Показаны {count} барбера',
    many: 'Показаны {count} барберов',
  },
  leadMinutes: {
    one: 'за {count} минуту',
    few: 'за {count} минуты',
    many: 'за {count} минут',
  },
}

const EN: Record<PluralKey, PluralMessage> = {
  reviews: {
    one: '{count} review',
    other: '{count} reviews',
  },
  profiles: {
    one: '{count} profile',
    other: '{count} profiles',
  },
  salons: {
    one: '{count} salon',
    other: '{count} salons',
  },
  bookings: {
    one: '{count} booking',
    other: '{count} bookings',
  },
  stuckBookings: {
    one: '{count} booking',
    other: '{count} bookings',
  },
  accounts: {
    one: '{count} account',
    other: '{count} accounts',
  },
  barbers: {
    one: '{count} barber',
    other: '{count} barbers',
  },
  events: {
    one: '{count} event',
    other: '{count} events',
  },
  gaps: {
    one: '{count} gap',
    other: '{count} gaps',
  },
  days: {
    one: '{count} day',
    other: '{count} days',
  },
  experience: {
    one: '{count} year',
    other: '{count} years',
  },
  visits: {
    one: '{count} visit',
    other: '{count} visits',
  },
  minutes: {
    one: '{count} minute',
    other: '{count} minutes',
  },
  hours: {
    one: '{count} hour',
    other: '{count} hours',
  },
  slots: {
    one: '{count} slot',
    other: '{count} slots',
  },
  rows: {
    one: '{count} row',
    other: '{count} rows',
  },
  shownBarbers: {
    one: 'Showing {count} barber',
    other: 'Showing {count} barbers',
  },
  leadMinutes: {
    one: 'in {count} minute',
    other: 'in {count} minutes',
  },
}

const FORMS: Record<Locale, Record<PluralKey, PluralMessage>> = {
  ru: RU,
  en: EN,
}

const rules = new Map<Locale, Intl.PluralRules>()

function pluralRules(locale: Locale): Intl.PluralRules {
  const cached = rules.get(locale)

  if (cached) {
    return cached
  }

  const created = new Intl.PluralRules(locale)
  rules.set(locale, created)

  return created
}

/** Выбранная форма счётчика; неизвестная категория падает на `many`/`other`. */
export function pluralMessage(
  locale: Locale,
  key: PluralKey,
  count: number
): string {
  const forms = FORMS[locale][key]
  const category = pluralRules(locale).select(count)

  return (
    forms[category as keyof PluralMessage] ??
    forms.other ??
    forms.many ??
    forms.few ??
    forms.one
  )
}
