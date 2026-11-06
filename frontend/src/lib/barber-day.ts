import type { BarberBooking } from '@/lib/barber-bookings'

/**
 * Лента дня кабинета барбера.
 *
 * Отдельного эндпоинта «день» нет: сервер уже сортирует так, что сегодняшние
 * строки всегда первые в обоих скоупах (`upcoming` — по возрастанию, `past` — по
 * убыванию). Поэтому день собирается из первых страниц двух списков и фильтра по
 * календарной дате браузера — здесь, чистыми функциями, без запросов.
 *
 * Пояс: дата дня — в поясе браузера (барбер смотрит свой телефон), а время строки
 * остаётся в поясе салона и форматируется уже в компоненте.
 */

/** Минимальное окно, которое стоит показывать: меньше — визуальный шум. */
export const MIN_VISIBLE_GAP_MINUTES = 20

export type DayGap = {
  /** ISO, конец предыдущей записи. */
  startAt: string
  /** ISO, начало следующей. */
  endAt: string
  minutes: number
}

// `en-CA` отдаёт `YYYY-MM-DD`, а без `timeZone` формат работает в поясе браузера —
// ровно том, в котором барбер читает «сегодня».
const localDayFormatter = new Intl.DateTimeFormat('en-CA', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

function localDayKey(value: number | string): string {
  const date = typeof value === 'number' ? new Date(value) : new Date(value)

  return localDayFormatter.format(date)
}

/** `YYYY-MM-DD` — календарная дата момента в поясе браузера. */
export function localDateKey(now: number): string {
  return localDayKey(now)
}

/** Одна календарная дата в поясе браузера. */
export function isSameLocalDay(iso: string, now: number): boolean {
  return localDayKey(iso) === localDayKey(now)
}

/**
 * Порядок ленты: строго по началу, одинаковое начало — по `id`.
 *
 * `Array.prototype.sort` стабилен, но порядок источника у двух списков разный,
 * поэтому `id` нужен как явный вторичный ключ.
 */
function compareBookings(left: BarberBooking, right: BarberBooking): number {
  const difference = Date.parse(left.startAt) - Date.parse(right.startAt)

  if (difference !== 0) {
    return difference
  }

  return left.id < right.id ? -1 : left.id > right.id ? 1 : 0
}

/**
 * Слияние двух списков в один день: фильтр, дедупликация по `id`, сортировка.
 *
 * Входные массивы не мутируются. При одинаковом `id` выигрывает строка из
 * `upcoming`: статус в ней свежее — терминальные строки живут только в `past`.
 */
export function mergeDayBookings(
  upcoming: readonly BarberBooking[],
  past: readonly BarberBooking[],
  now: number
): BarberBooking[] {
  const byId = new Map<string, BarberBooking>()

  for (const booking of past) {
    if (isSameLocalDay(booking.startAt, now)) {
      byId.set(booking.id, booking)
    }
  }

  for (const booking of upcoming) {
    if (isSameLocalDay(booking.startAt, now)) {
      byId.set(booking.id, booking)
    }
  }

  return [...byId.values()].sort(compareBookings)
}

/**
 * Окна между соседними записями.
 *
 * Границы дня окнами не считаются: рабочие часы требуют расписания по каждому
 * салону, это отдельный срез. Пересечение (одна запись внутри другой) и микроокно
 * меньше `MIN_VISIBLE_GAP_MINUTES` окном не являются.
 */
export function findDayGaps(bookings: readonly BarberBooking[]): DayGap[] {
  const ordered = [...bookings].sort(compareBookings)
  const gaps: DayGap[] = []

  for (let index = 1; index < ordered.length; index += 1) {
    const previous = ordered[index - 1]
    const next = ordered[index]
    const minutes = Math.round(
      (Date.parse(next.startAt) - Date.parse(previous.endAt)) / 60_000
    )

    if (minutes < MIN_VISIBLE_GAP_MINUTES) {
      continue
    }

    gaps.push({ startAt: previous.endAt, endAt: next.startAt, minutes })
  }

  return gaps
}

/**
 * Ближайшая запись, которая ещё не закончилась.
 *
 * Статус не важен: завершённая строка остаётся в ленте дня, но «следующей» уже не
 * считается — её конец в прошлом. Идущий визит тоже подходит: его `endAt` ещё
 * впереди.
 */
export function findNextBooking(
  bookings: readonly BarberBooking[],
  now: number
): BarberBooking | null {
  const ordered = [...bookings].sort(compareBookings)

  for (const booking of ordered) {
    if (Date.parse(booking.endAt) > now) {
      return booking
    }
  }

  return null
}
