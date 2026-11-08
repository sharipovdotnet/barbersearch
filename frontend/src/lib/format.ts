/**
 * Форматирование дат, времени и денег для кабинета.
 *
 * Время визита приходит в UTC, а показывать его нужно в поясе салона: только там
 * «18:30» совпадает с тем, что клиент увидел при выборе слота. Поэтому каждая функция
 * принимает IANA-идентификатор пояса и падает обратно на пояс браузера, если сервер
 * прислал что-то неизвестное.
 *
 * Язык берётся из i18n: форматтеры кэшируются отдельно для `ru-RU` и `en-GB`.
 * Английский вариант — `en-GB`, чтобы сохранить круглосуточное время и порядок
 * «день-месяц», привычный русской версии.
 */

import { getLocale, localeTag, t, tp, type PluralKey } from '@/i18n'

const priceFormatters = new Map<string, Intl.NumberFormat>()

function priceFormatter(): Intl.NumberFormat {
  const tag = localeTag(getLocale())
  const cached = priceFormatters.get(tag)

  if (cached) {
    return cached
  }

  const formatter = new Intl.NumberFormat(tag, {
    style: 'currency',
    currency: 'RUB',
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  })

  priceFormatters.set(tag, formatter)

  return formatter
}

const formatterCache = new Map<string, Intl.DateTimeFormat>()
const dayKeyCache = new Map<string, Intl.DateTimeFormat>()

function resolveFormatter(
  options: Intl.DateTimeFormatOptions,
  timeZone?: string
): Intl.DateTimeFormat {
  const locale = getLocale()
  const key = `${locale}|${timeZone ?? 'local'}|${JSON.stringify(options)}`
  const cached = formatterCache.get(key)

  if (cached) {
    return cached
  }

  let formatter: Intl.DateTimeFormat

  try {
    formatter = new Intl.DateTimeFormat(localeTag(locale), {
      ...options,
      ...(timeZone ? { timeZone } : {}),
    })
  } catch {
    // Неизвестный пояс салона не должен ломать страницу: показываем пояс браузера.
    formatter = new Intl.DateTimeFormat(localeTag(locale), options)
  }

  formatterCache.set(key, formatter)
  return formatter
}

function resolveDayKeyFormatter(timeZone?: string): Intl.DateTimeFormat {
  const key = timeZone ?? 'local'
  const cached = dayKeyCache.get(key)

  if (cached) {
    return cached
  }

  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }

  let formatter: Intl.DateTimeFormat

  try {
    formatter = new Intl.DateTimeFormat('en-CA', {
      ...options,
      ...(timeZone ? { timeZone } : {}),
    })
  } catch {
    formatter = new Intl.DateTimeFormat('en-CA', options)
  }

  dayKeyCache.set(key, formatter)
  return formatter
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

/** `2400` → «2 400 ₽» / «RUB 2,400». Копейки показываются только если они есть. */
export function formatPrice(value: number): string {
  return priceFormatter().format(value)
}

/** «18:30» в поясе салона. */
export function formatTime(iso: string, timeZone?: string): string {
  return resolveFormatter(
    { hour: '2-digit', minute: '2-digit' },
    timeZone
  ).format(new Date(iso))
}

/**
 * «09:00» из `TimeOnly` расписания.
 *
 * `formatTime` здесь не подходит: сервер присылает время локальным для салона и без
 * даты (`09:00:00`), а `new Date` такого не понимает. Пересчитывать пояс не нужно —
 * расписание уже в поясе салона.
 */
export function formatScheduleTime(value: string): string {
  const match = /^(\d{2}):(\d{2})/.exec(value)

  return match ? `${match[1]}:${match[2]}` : value
}

/** «сб, 1 ноября» / «Sun 1 November» — для героя-карточки. */
export function formatLongDay(iso: string, timeZone?: string): string {
  return resolveFormatter(
    { weekday: 'short', day: 'numeric', month: 'long' },
    timeZone
  ).format(new Date(iso))
}

/**
 * «понедельник, 2 ноября» из календарной даты `YYYY-MM-DD`.
 *
 * `formatLongDay` здесь не подходит: `YYYY-MM-DD` — не ISO-инстант, и в поясах
 * западнее UTC он показал бы предыдущий день. Полночь UTC и `timeZone: 'UTC'`
 * дают ровно ту дату, которую выбрали.
 */
export function formatSalonDate(date: string): string {
  return resolveFormatter(
    { weekday: 'long', day: 'numeric', month: 'long' },
    'UTC'
  ).format(new Date(`${date}T00:00:00Z`))
}

/** «1 нояб.» / «1 Nov» — компактная дата строки списка. */
export function formatShortDay(iso: string, timeZone?: string): string {
  return resolveFormatter(
    { day: 'numeric', month: 'short' },
    timeZone
  ).format(new Date(iso))
}

/** «сб» / «Sat» — день недели строки списка. */
export function formatWeekday(iso: string, timeZone?: string): string {
  return resolveFormatter({ weekday: 'short' }, timeZone).format(new Date(iso))
}

/**
 * «18 октября 2026» / «18 October 2026» — календарная дата отзыва без времени.
 *
 * `formatTime`/`formatShortDay` здесь не подходят: отзыву нужен год, а пояс салона
 * переносит ночные инстанты на соседний день. `ru-RU` добавляет «г.» к году — в
 * карточке отзыва он лишний.
 */
export function formatReviewDate(iso: string, timeZone?: string): string {
  return resolveFormatter(
    { day: 'numeric', month: 'long', year: 'numeric' },
    timeZone
  )
    .format(new Date(iso))
    .replace(/\s*г\.$/, '')
}

/** «Ноябрь 2026» / «November 2026» — заголовок группы. */
export function formatMonth(iso: string, timeZone?: string): string {
  const date = new Date(iso)
  const month = resolveFormatter({ month: 'long' }, timeZone).format(date)
  const year = resolveFormatter({ year: 'numeric' }, timeZone).format(date)

  return `${capitalize(month)} ${year}`
}

function dayKey(date: Date, timeZone?: string): string {
  return resolveDayKeyFormatter(timeZone).format(date)
}

const DAY_MS = 24 * 60 * 60 * 1000

/** Разница в календарных днях между двумя моментами в поясе салона. */
function dayDifference(
  iso: string,
  now: number,
  timeZone?: string
): number {
  const target = Date.parse(`${dayKey(new Date(iso), timeZone)}T00:00:00Z`)
  const today = Date.parse(`${dayKey(new Date(now), timeZone)}T00:00:00Z`)

  return Math.round((target - today) / DAY_MS)
}

/** «Сегодня» / «Завтра» либо `null`, если визит дальше. */
export function relativeDayLabel(
  iso: string,
  now: number,
  timeZone?: string
): string | null {
  const difference = dayDifference(iso, now, timeZone)

  if (difference === 0) {
    return t('Сегодня')
  }

  if (difference === 1) {
    return t('Завтра')
  }

  return null
}

/**
 * «через 2 часа», «меньше минуты» либо `null`, если до визита больше недели:
 * точный обратный отсчёт на таком горизонте только мешает.
 */
export function formatCountdown(iso: string, now: number): string | null {
  const difference = Date.parse(iso) - now

  if (difference <= 0) {
    return null
  }

  const minutes = Math.round(difference / 60_000)

  if (minutes < 1) {
    return t('меньше минуты')
  }

  if (minutes < 60) {
    return t('через {value}', { value: tp('minutes', minutes) })
  }

  const hours = Math.round(minutes / 60)

  if (hours < 24) {
    return t('через {value}', { value: tp('hours', hours) })
  }

  const days = Math.round(hours / 24)

  if (days <= 7) {
    return t('через {value}', { value: tp('days', days) })
  }

  return null
}

/** «60 мин» / «1 ч 30 мин» — длительность услуги. */
export function formatDuration(totalMinutes: number): string {
  if (totalMinutes <= 0) {
    return ''
  }

  if (totalMinutes < 60) {
    return `${totalMinutes} ${t('мин')}`
  }

  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60

  return minutes === 0
    ? `${hours} ${t('ч')}`
    : `${hours} ${t('ч')} ${minutes} ${t('мин')}`
}

/** «1 ноября в 18:30» / «1 November at 18:30» — для подтверждений и объявлений. */
export function formatDateTime(
  iso: string,
  timeZone?: string
): string {
  return t('{date} в {time}', {
    date: formatLongDay(iso, timeZone),
    time: formatTime(iso, timeZone),
  })
}

/** «1 нояб. 18:30» — компактные дата и время строки списка. */
export function formatShortDateTime(iso: string, timeZone?: string): string {
  return `${formatShortDay(iso, timeZone)} ${formatTime(iso, timeZone)}`
}

const ratingFormatters = new Map<string, Intl.NumberFormat>()

function ratingFormatter(): Intl.NumberFormat {
  const tag = localeTag(getLocale())
  const cached = ratingFormatters.get(tag)

  if (cached) {
    return cached
  }

  const formatter = new Intl.NumberFormat(tag, {
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  })

  ratingFormatters.set(tag, formatter)

  return formatter
}

const clockFormatters = new Map<string, Intl.DateTimeFormat>()

function clockFormatter(): Intl.DateTimeFormat {
  const tag = localeTag(getLocale())
  const cached = clockFormatters.get(tag)

  if (cached) {
    return cached
  }

  const formatter = new Intl.DateTimeFormat(tag, {
    hour: '2-digit',
    minute: '2-digit',
  })

  clockFormatters.set(tag, formatter)

  return formatter
}

/** «4,8» / «4.8»; для отсутствующего рейтинга — прочерк. */
export function formatRating(value: number | null): string {
  return value === null ? '—' : ratingFormatter().format(value)
}

/** «24 отзыва» / «24 reviews». */
export function formatReviewsCount(count: number): string {
  return tp('reviews', count)
}

/** «2 профиля» / «2 profiles» — очередь «Барберы без подтверждения». */
export function formatProfilesCount(count: number): string {
  return tp('profiles', count)
}

/** «2 салона» / «2 salons» — блок «Выключенные салоны» сводки. */
export function formatSalonsCount(count: number): string {
  return tp('salons', count)
}

/** «20 записей» / «20 bookings». */
export function formatBookingsCount(count: number): string {
  return tp('bookings', count)
}

/** «3 события» / «3 events» — «Пропущено N событий» в импорте. */
export function formatEventsCount(count: number): string {
  return tp('events', count)
}

const numberFormatters = new Map<string, Intl.NumberFormat>()

function numberFormatter(minimum: number, maximum: number): Intl.NumberFormat {
  const tag = localeTag(getLocale())
  const key = `${tag}|${minimum}|${maximum}`
  const cached = numberFormatters.get(key)

  if (cached) {
    return cached
  }

  const formatter = new Intl.NumberFormat(tag, {
    minimumFractionDigits: minimum,
    maximumFractionDigits: maximum,
  })

  numberFormatters.set(key, formatter)

  return formatter
}

/** «12 480» / «12,480» — число в типографике языка. */
export function formatNumber(value: number, digits = 0): string {
  return numberFormatter(digits, digits).format(value)
}

/**
 * Доля `0..1` → «4,7 %» / «4.7 %»; `null` — «—», потому что ноль здесь был бы враньём.
 */
export function formatPercent(value: number | null, digits = 1): string {
  if (value === null) {
    return '—'
  }

  const formatted = numberFormatter(0, digits).format(value * 100)

  return getLocale() === 'ru' ? `${formatted} %` : `${formatted}%`
}

/**
 * «12,3» — гибкая дробная часть: лишний ноль не печатается.
 *
 * Дельты бывают и целыми (`−10 %`), и дробными (`+0,8 п.п.`); фиксированное
 * число знаков дало бы «−10,0 %» и шум в плитке.
 */
function formatFlexible(value: number, maxDigits = 1): string {
  const tag = localeTag(getLocale())
  const key = `flex|${tag}|${maxDigits}`
  const cached = numberFormatters.get(key)

  if (cached) {
    return cached.format(value)
  }

  const formatter = new Intl.NumberFormat(tag, {
    maximumFractionDigits: maxDigits,
  })

  numberFormatters.set(key, formatter)

  return formatter.format(value)
}

/**
 * Дельта к прошлому периоду: проценты для счётчиков и денег, п.п. для долей.
 *
 * `null`/`0` в прошлом периоде дают «—»: процент от нулевой базы не определён.
 * Знак — единственный носитель направления, поэтому он всегда виден текстом,
 * а не только цветом.
 */
export function formatDelta(
  current: number | null,
  previous: number | null,
  kind: 'percent' | 'points' = 'percent'
): string {
  if (current === null || previous === null) {
    return '—'
  }

  const delta =
    kind === 'points'
      ? (current - previous) * 100
      : previous === 0
        ? null
        : ((current - previous) / Math.abs(previous)) * 100

  if (delta === null || !Number.isFinite(delta)) {
    return '—'
  }

  const rounded = Math.abs(delta) < 0.05 ? 0 : delta
  const sign = rounded > 0 ? '+' : rounded < 0 ? '−' : '±'
  const value = formatFlexible(Math.abs(rounded))

  if (kind === 'points') {
    return `${sign}${value} ${t('п.п.')}`
  }

  return getLocale() === 'ru' ? `${sign}${value} %` : `${sign}${value}%`
}

/**
 * Формы счётчиков, которые приходят из статистики как русские тройки.
 *
 * Публичная сигнатура `formatCount` осталась прежней, чтобы не менять вызовы;
 * первая форма — ключ к смысловому склонению. Неизвестная тройка остаётся как есть:
 * это сигнал, что новый счётчик нужно добавить в `plurals.ts`.
 */
const COUNT_KEYS: Record<string, PluralKey> = {
  визит: 'visits',
  запись: 'bookings',
  бронь: 'stuckBookings',
  аккаунт: 'accounts',
  отзыв: 'reviews',
  профиль: 'profiles',
  салон: 'salons',
  событие: 'events',
  окно: 'gaps',
  день: 'days',
}

/** «3 визита»: та же плюрализация, что у остальных счётчиков. */
export function formatCount(
  count: number,
  forms: readonly [string, string, string]
): string {
  const key = COUNT_KEYS[forms[0]]

  if (key) {
    return tp(key, count)
  }

  return `${count} ${forms[0]}`
}

/** «2 окна» / «2 gaps» — свободные промежутки в ленте дня. */
export function formatGapsCount(count: number): string {
  return tp('gaps', count)
}

/** «5 дней» / «5 days» — рабочее расписание в чек-листе готовности. */
export function formatDaysCount(count: number): string {
  return tp('days', count)
}

/** «8 лет» / «8 years» — стаж барбера. */
export function formatExperience(years: number): string {
  return tp('experience', years)
}

/** Сколько отзыв ждёт решения: «ждёт 3 дня» / «waiting 3 days». */
export function formatWaitingSince(iso: string, now: number): string {
  const difference = Math.max(0, now - Date.parse(iso))
  const minutes = Math.floor(difference / 60_000)

  if (minutes < 1) {
    return t('только что')
  }

  if (minutes < 60) {
    return t('ждёт {value}', { value: tp('minutes', minutes) })
  }

  const hours = Math.floor(minutes / 60)

  if (hours < 24) {
    return t('ждёт {value}', { value: tp('hours', hours) })
  }

  const days = Math.floor(hours / 24)
  return t('ждёт {value}', { value: tp('days', days) })
}

/** «Дмитрий Крылов» → «Дмитрий К.» — строка очереди не должна расти. */
export function formatShortName(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean)

  if (words.length === 0) {
    return t('Клиент')
  }

  if (words.length === 1) {
    return words[0]
  }

  return `${words[0]} ${words[1].charAt(0).toUpperCase()}.`
}

/** «18:42» — время последнего успешного обновления. */
export function formatClockTime(value: string | Date): string {
  return clockFormatter().format(
    typeof value === 'string' ? new Date(value) : value
  )
}

/** «Артём Соколов» → «АС»; для аватара, когда фотографии нет. */
export function getInitials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)

  if (words.length === 0) {
    return '?'
  }

  const letters = words.length === 1 ? [words[0]] : [words[0], words[1]]

  return letters
    .map((word) => word.charAt(0).toUpperCase())
    .join('')
}
