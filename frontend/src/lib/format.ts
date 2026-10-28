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

/** «сб, 1 ноября» — для героя-карточки. */
export function formatLongDay(iso: string, timeZone?: string): string {
  return resolveFormatter(
    { weekday: 'short', day: 'numeric', month: 'long' },
    timeZone
  ).format(new Date(iso))
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
