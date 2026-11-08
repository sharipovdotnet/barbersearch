/**
 * Форматирование дат, времени и денег для кабинета.
 *
 * Время визита приходит в UTC, а показывать его нужно в поясе салона: только там
 * «18:30» совпадает с тем, что клиент увидел при выборе слота. Поэтому каждая функция
 * принимает IANA-идентификатор пояса и падает обратно на пояс браузера, если сервер
 * прислал что-то неизвестное.
 */

const priceFormatter = new Intl.NumberFormat('ru-RU', {
  style: 'currency',
  currency: 'RUB',
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
})

const formatterCache = new Map<string, Intl.DateTimeFormat>()
const dayKeyCache = new Map<string, Intl.DateTimeFormat>()

function resolveFormatter(
  options: Intl.DateTimeFormatOptions,
  timeZone?: string
): Intl.DateTimeFormat {
  const key = `${timeZone ?? 'local'}|${JSON.stringify(options)}`
  const cached = formatterCache.get(key)

  if (cached) {
    return cached
  }

  let formatter: Intl.DateTimeFormat

  try {
    formatter = new Intl.DateTimeFormat('ru-RU', {
      ...options,
      ...(timeZone ? { timeZone } : {}),
    })
  } catch {
    // Неизвестный пояс салона не должен ломать страницу: показываем пояс браузера.
    formatter = new Intl.DateTimeFormat('ru-RU', options)
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

/** `2400` → «2 400 ₽». Копейки показываются только если они есть. */
export function formatPrice(value: number): string {
  return priceFormatter.format(value)
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

/** «сб, 1 ноября» — для героя-карточки. */
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

/** «1 нояб.» — компактная дата строки списка. */
export function formatShortDay(iso: string, timeZone?: string): string {
  return resolveFormatter(
    { day: 'numeric', month: 'short' },
    timeZone
  ).format(new Date(iso))
}

/** «сб» — день недели строки списка. */
export function formatWeekday(iso: string, timeZone?: string): string {
  return resolveFormatter({ weekday: 'short' }, timeZone).format(new Date(iso))
}

/**
 * «18 октября 2026» — календарная дата отзыва без времени.
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

/** «Ноябрь 2026» — заголовок группы. */
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
    return 'Сегодня'
  }

  if (difference === 1) {
    return 'Завтра'
  }

  return null
}

function plural(count: number, forms: readonly [string, string, string]) {
  const mod10 = count % 10
  const mod100 = count % 100

  if (mod10 === 1 && mod100 !== 11) {
    return forms[0]
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return forms[1]
  }

  return forms[2]
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
    return 'меньше минуты'
  }

  if (minutes < 60) {
    return `через ${minutes} ${plural(minutes, ['минуту', 'минуты', 'минут'])}`
  }

  const hours = Math.round(minutes / 60)

  if (hours < 24) {
    return `через ${hours} ${plural(hours, ['час', 'часа', 'часов'])}`
  }

  const days = Math.round(hours / 24)

  if (days <= 7) {
    return `через ${days} ${plural(days, ['день', 'дня', 'дней'])}`
  }

  return null
}

/** «60 мин» / «1 ч 30 мин» — длительность услуги. */
export function formatDuration(totalMinutes: number): string {
  if (totalMinutes <= 0) {
    return ''
  }

  if (totalMinutes < 60) {
    return `${totalMinutes} мин`
  }

  const hours = Math.floor(totalMinutes / 60)
  const minutes = totalMinutes % 60

  return minutes === 0 ? `${hours} ч` : `${hours} ч ${minutes} мин`
}

/** «1 ноября в 18:30» — для текстов подтверждения и объявлений. */
export function formatDateTime(
  iso: string,
  timeZone?: string
): string {
  return `${formatLongDay(iso, timeZone)} в ${formatTime(iso, timeZone)}`
}

/** «1 нояб. 18:30» — компактные дата и время строки списка. */
export function formatShortDateTime(iso: string, timeZone?: string): string {
  return `${formatShortDay(iso, timeZone)} ${formatTime(iso, timeZone)}`
}

const ratingFormatter = new Intl.NumberFormat('ru-RU', {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
})

const clockFormatter = new Intl.DateTimeFormat('ru-RU', {
  hour: '2-digit',
  minute: '2-digit',
})

/** «4,8»; для отсутствующего рейтинга — прочерк. */
export function formatRating(value: number | null): string {
  return value === null ? '—' : ratingFormatter.format(value)
}

/** «24 отзыва», «1 отзыв», «21 отзыв». */
export function formatReviewsCount(count: number): string {
  return `${count} ${plural(count, ['отзыв', 'отзыва', 'отзывов'])}`
}

/** «2 профиля», «1 профиль», «5 профилей» — очередь «Барберы без подтверждения». */
export function formatProfilesCount(count: number): string {
  return `${count} ${plural(count, ['профиль', 'профиля', 'профилей'])}`
}

/** «2 салона», «1 салон», «5 салонов» — блок «Выключенные салоны» сводки. */
export function formatSalonsCount(count: number): string {
  return `${count} ${plural(count, ['салон', 'салона', 'салонов'])}`
}

/** «20 записей», «1 запись», «3 записи». */
export function formatBookingsCount(count: number): string {
  return `${count} ${plural(count, ['запись', 'записи', 'записей'])}`
}

/** «3 события», «1 событие», «5 событий» — «Пропущено N событий» в импорте. */
export function formatEventsCount(count: number): string {
  return `${count} ${plural(count, ['событие', 'события', 'событий'])}`
}

const numberFormatters = new Map<string, Intl.NumberFormat>()

function numberFormatter(minimum: number, maximum: number): Intl.NumberFormat {
  const key = `${minimum}|${maximum}`
  const cached = numberFormatters.get(key)

  if (cached) {
    return cached
  }

  const formatter = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: minimum,
    maximumFractionDigits: maximum,
  })

  numberFormatters.set(key, formatter)

  return formatter
}

/** «12 480» / «26,5» — число в русской типографике. */
export function formatNumber(value: number, digits = 0): string {
  return numberFormatter(digits, digits).format(value)
}

/**
 * Доля `0..1` → «4,7 %»; `null` — «—», потому что ноль здесь был бы враньём.
 */
export function formatPercent(value: number | null, digits = 1): string {
  if (value === null) {
    return '—'
  }

  return numberFormatter(0, digits).format(value * 100) + ' %'
}

/**
 * «12,3» — гибкая дробная часть: лишний ноль не печатается.
 *
 * Дельты бывают и целыми (`−10 %`), и дробными (`+0,8 п.п.`); фиксированное
 * число знаков дало бы «−10,0 %» и шум в плитке.
 */
function formatFlexible(value: number, maxDigits = 1): string {
  const key = `flex|${maxDigits}`
  const cached = numberFormatters.get(key)

  if (cached) {
    return cached.format(value)
  }

  const formatter = new Intl.NumberFormat('ru-RU', {
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

  return `${sign}${formatFlexible(Math.abs(rounded))} ${kind === 'points' ? 'п.п.' : '%'}`
}

/** «3 визита»: та же плюрализация, что у остальных счётчиков. */
export function formatCount(
  count: number,
  forms: readonly [string, string, string]
): string {
  return `${count} ${plural(count, forms)}`
}

/** «2 окна», «1 окно», «5 окон» — свободные промежутки в ленте дня. */
export function formatGapsCount(count: number): string {
  return `${count} ${plural(count, ['окно', 'окна', 'окон'])}`
}

/** «5 дней», «1 день», «2 дня» — рабочее расписание в чек-листе готовности. */
export function formatDaysCount(count: number): string {
  return `${count} ${plural(count, ['день', 'дня', 'дней'])}`
}

/** «8 лет», «1 год», «2 года» — стаж барбера. */
export function formatExperience(years: number): string {
  return `${years} ${plural(years, ['год', 'года', 'лет'])}`
}

/** Сколько отзыв ждёт решения: «ждёт 3 дня», «ждёт 6 часов». */
export function formatWaitingSince(iso: string, now: number): string {
  const difference = Math.max(0, now - Date.parse(iso))
  const minutes = Math.floor(difference / 60_000)

  if (minutes < 1) {
    return 'только что'
  }

  if (minutes < 60) {
    return `ждёт ${minutes} ${plural(minutes, ['минуту', 'минуты', 'минут'])}`
  }

  const hours = Math.floor(minutes / 60)

  if (hours < 24) {
    return `ждёт ${hours} ${plural(hours, ['час', 'часа', 'часов'])}`
  }

  const days = Math.floor(hours / 24)
  return `ждёт ${days} ${plural(days, ['день', 'дня', 'дней'])}`
}

/** «Дмитрий Крылов» → «Дмитрий К.» — строка очереди не должна расти. */
export function formatShortName(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean)

  if (words.length === 0) {
    return 'Клиент'
  }

  if (words.length === 1) {
    return words[0]
  }

  return `${words[0]} ${words[1].charAt(0).toUpperCase()}.`
}

/** «18:42» — время последнего успешного обновления. */
export function formatClockTime(value: string | Date): string {
  return clockFormatter.format(typeof value === 'string' ? new Date(value) : value)
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
