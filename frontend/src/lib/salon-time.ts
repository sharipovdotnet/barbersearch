/**
 * Локальное время салона → UTC-инстант.
 *
 * Сервер хранит визиты в UTC, а барбер вставляет список с временем по часам салона.
 * `Date` знает только пояс браузера, поэтому смещение вычисляется через `Intl`:
 * инстант форматируется в поясе салона, полученная «настенная» дата сравнивается
 * с исходной, и разница уходит в поправку. Без внешних библиотек и без второго
 * прохода по времени суток.
 */

const DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/
const TIME_PATTERN = /^(\d{2}):(\d{2})$/

/**
 * Проба смещения вокруг «настенного» времени.
 *
 * Смещение пояса меняется не чаще двух раз в год и всегда меньше суток, поэтому
 * сутки с запасом накрывают любой переход: пробы до и после него дают обе
 * возможные поправки — это и нужно для неоднозначного часа.
 */
const PROBE_MS = 26 * 60 * 60 * 1000

const formatterCache = new Map<string, Intl.DateTimeFormat | null>()

function resolveFormatter(timeZoneId: string): Intl.DateTimeFormat | null {
  const cached = formatterCache.get(timeZoneId)

  if (cached !== undefined) {
    return cached
  }

  let formatter: Intl.DateTimeFormat | null = null

  try {
    formatter = new Intl.DateTimeFormat('en-CA', {
      timeZone: timeZoneId,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    })
  } catch {
    // Битый исторический пояс ломает `Intl` исключением. Времени в таком поясе
    // не существует, но страница не должна падать: вызывающий получит `null`.
    formatter = null
  }

  formatterCache.set(timeZoneId, formatter)
  return formatter
}

/**
 * «Настенные» часы пояса в миллисекундах: те же компоненты, но прочитанные как UTC.
 *
 * Разница `wallClock - instant` и есть смещение пояса в этот момент.
 */
function wallClock(
  instant: number,
  timeZoneId: string
): number | null {
  const formatter = resolveFormatter(timeZoneId)

  if (formatter === null) {
    return null
  }

  const parts = formatter.formatToParts(new Date(instant))
  const values = new Map<string, number>()

  for (const part of parts) {
    if (part.type === 'literal') {
      continue
    }

    values.set(part.type, Number(part.value))
  }

  const year = values.get('year')
  const month = values.get('month')
  const day = values.get('day')
  const hour = values.get('hour')
  const minute = values.get('minute')

  if (
    year === undefined
    || month === undefined
    || day === undefined
    || hour === undefined
    || minute === undefined
  ) {
    return null
  }

  return Date.UTC(year, month - 1, day, hour, minute)
}

function parseDate(
  date: string
): { year: number; month: number; day: number } | null {
  const match = DATE_PATTERN.exec(date)

  if (match === null) {
    return null
  }

  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])

  // `Date.UTC` переносит 31 февраля на 3 марта: такую дату в списке молча
  // превращать в другую нельзя, поэтому компоненты проверяются обратно.
  const probe = new Date(Date.UTC(year, month - 1, day))

  if (
    probe.getUTCFullYear() !== year
    || probe.getUTCMonth() !== month - 1
    || probe.getUTCDate() !== day
  ) {
    return null
  }

  return { year, month, day }
}

function parseTime(time: string): { hours: number; minutes: number } | null {
  const match = TIME_PATTERN.exec(time)

  if (match === null) {
    return null
  }

  const hours = Number(match[1])
  const minutes = Number(match[2])

  if (hours > 23 || minutes > 59) {
    return null
  }

  return { hours, minutes }
}

/**
 * Локальные дата и время салона → UTC ISO. `null`, если такого времени нет.
 *
 * Весной при переходе на летнее время локальный час пропадает: обратное
 * форматирование не даёт исходные компоненты ни с одной поправкой — тогда `null`,
 * и строка импорта получает `badTime`. Осенью час повторяется: годятся обе
 * поправки, берётся **первое** вхождение (то есть ещё летнее смещение).
 */
export function salonDateTimeToUtc(
  date: string,
  time: string,
  timeZoneId: string
): string | null {
  const parsedDate = parseDate(date)
  const parsedTime = parseTime(time)

  if (parsedDate === null || parsedTime === null) {
    return null
  }

  const naive = Date.UTC(
    parsedDate.year,
    parsedDate.month - 1,
    parsedDate.day,
    parsedTime.hours,
    parsedTime.minutes
  )

  const offsets = new Set<number>()

  for (const probe of [naive - PROBE_MS, naive, naive + PROBE_MS]) {
    const wall = wallClock(probe, timeZoneId)

    if (wall === null) {
      return null
    }

    offsets.add(wall - probe)
  }

  let first: number | null = null

  for (const offset of offsets) {
    const candidate = naive - offset

    // Кандидат годится, только если в поясе он снова показывает ровно эти
    // дату и время: иначе это дыра перехода.
    if (wallClock(candidate, timeZoneId) !== naive) {
      continue
    }

    if (first === null || candidate < first) {
      first = candidate
    }
  }

  return first === null ? null : new Date(first).toISOString()
}

/**
 * Обёртка над `wallClock` для импорта из календаря: проверяет инстант до `Intl`.
 *
 * `formatToParts` бросает на невалидной дате, а файл — недоверенный ввод. `null`
 * означает «пояс или дата не прочитались»: вызывающий берёт срез исходного ISO.
 */
function salonWallClock(iso: string, timeZoneId: string): number | null {
  const instant = Date.parse(iso)

  if (Number.isNaN(instant)) {
    return null
  }

  try {
    return wallClock(instant, timeZoneId)
  } catch {
    // Защита от исключения `Intl`: решение о срезе ISO принимает вызывающий.
    return null
  }
}

/**
 * Календарная дата `YYYY-MM-DD` инстанта в поясе салона.
 *
 * Событие календаря хранится абсолютным моментом, а строка предпросмотра
 * показывает дату салона: в поясах восточнее UTC вечерний инстант уже завтра.
 * Неизвестный пояс не роняет страницу — возвращается дата самого ISO.
 */
export function salonDateFromInstant(iso: string, timeZoneId: string): string {
  const wall = salonWallClock(iso, timeZoneId)

  return wall === null ? iso.slice(0, 10) : new Date(wall).toISOString().slice(0, 10)
}

/**
 * Время `HH:mm` инстанта в поясе салона.
 *
 * UI показывает минуты без секунд, а правка времени потом пересчитывает момент
 * из пояса выбранного салона. Неизвестный пояс — срез ISO, как у `salonDateFromInstant`.
 */
export function salonTimeFromInstant(iso: string, timeZoneId: string): string {
  const wall = salonWallClock(iso, timeZoneId)

  return wall === null ? iso.slice(11, 16) : new Date(wall).toISOString().slice(11, 16)
}
