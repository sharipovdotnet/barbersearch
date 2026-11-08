/**
 * Разбор выгруженного календаря (`.ics`) в абсолютные вхождения.
 *
 * `ical.js` грузится динамически внутри `parseIcs`: вставка списка не должна
 * тянуть календарный парсер в основной чанк. Всё остальное здесь — чистые
 * функции над разобранным деревом, без сети, React и `localStorage`.
 *
 * ## Часовые пояса
 *
 * `VTIMEZONE` файла регистрируются в `ICAL.TimezoneService` **до** первого
 * чтения `DTSTART`/`DTEND`, иначе `ical.js` не применит пояс к датам. Если
 * пояс разрешён (`time.zone.tzid !== 'floating'`, это и зарегистрированный
 * `VTIMEZONE`, и компонент, найденный в дереве календаря), берём
 * `toJSDate()`. Если `TZID` есть, а пояс не разрешён (в файле нет
 * `VTIMEZONE`), строим момент из компонентов `year/month/day/hour/minute` в
 * этом IANA-поясе через `salonDateTimeToUtc`: `toJSDate()` применил бы пояс
 * браузера и молча сдвинул бы время. Floating-время (`TZID` нет) читается
 * как настенное время основного салона.
 *
 * ## Окно и сдвиг старта серии
 *
 * `event.iterator(startTime)` **заменяет** `DTSTART` серии переданным
 * временем внутри `RecurExpansion`. Поэтому «начать с начала окна» нельзя:
 * у недельной серии сломается день недели, у ежедневной — время суток.
 * Ограниченные серии (`COUNT`, `UNTIL`, `RDATE`) и серии с `INTERVAL > 1`
 * разворачиваются от настоящего `DTSTART` (их немного, а фаза зависит от
 * `DTSTART`). Бесконечная серия с `INTERVAL = 1` начинается со сдвинутой
 * копии `DTSTART`: та же зона и то же время суток, но настенная дата не
 * раньше начала окна и в фазе правила (`FREQ`, `BYDAY`, `BYMONTHDAY`).
 * Именно это позволяет разобрать ежедневную серию 2015 года, не потратив
 * лимит итераций на десять лет истории.
 *
 * `ICAL.TimezoneService` — глобальный singleton, а разбор не реентерабелен:
 * два файла одновременно разбирать нельзя (кнопка в UI блокируется). В
 * `finally` сервис сбрасывается, чтобы `VTIMEZONE` одного файла не влияли на
 * следующий.
 */

import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import { salonDateTimeToUtc } from '@/lib/salon-time'

export type IcsPeriodDays = 7 | 30 | 90

export type IcsSkippedReason =
  | 'allDay'
  | 'cancelled'
  | 'past'
  | 'outsidePeriod'
  | 'tooLong'
  | 'unknownTimeZone'
  | 'noUid'
  | 'invalid'

export type IcsSkippedEvent = {
  reason: IcsSkippedReason
  summary: string
  startAt: string | null
}

export type IcsParseFailure =
  | 'tooLarge'
  | 'invalidEncoding'
  | 'notCalendar'
  | 'invalid'
  | 'empty'
  | 'noEventsInPeriod'
  | 'tooManyEvents'
  | 'tooManyOccurrences'
  | null

export type IcsOccurrence = {
  uid: string
  /** UTC ISO или строка recurrence-id; `null` для одиночного события. */
  recurrenceId: string | null
  summary: string
  description: string
  location: string
  categories: string
  /** Абсолютный момент начала в UTC. */
  startAt: string
  /** Абсолютный конец или `null`, если в событии нет валидного конца. */
  endAt: string | null
  /** Длительность события, если она валидна (`0 < d ≤ 720` минут). */
  durationMinutes: number | null
  /** `TZID` события, `'UTC'` или `null` для floating. */
  sourceTimeZone: string | null
  isRecurring: boolean
  status: 'confirmed' | 'tentative'
  /** `TRANSP:TRANSPARENT` — «свободно»; не skip, а строка по умолчанию без галочки. */
  isFree: boolean
  sequence: number
}

export type IcsParseResult = {
  occurrences: IcsOccurrence[]
  skipped: IcsSkippedEvent[]
  failure: IcsParseFailure
}

export type IcsParseOptions = {
  services: readonly BarberProfileService[]
  salons: readonly BarberProfileSalon[]
  /** Основной салон: primary, иначе первый. */
  defaultSalonId: string
  now: number
  periodDays: IcsPeriodDays
}

/** Предел числа `VEVENT` в файле до раскрытия повторений. */
export const ICS_MAX_VEVENTS = 5000
/** Предел числа вхождений в окне после раскрытия. */
export const ICS_MAX_OCCURRENCES = 500
/** Предел шагов итератора на одну серию. */
export const ICS_MAX_ITERATIONS_PER_SERIES = 2000
/** Предел длительности одного события; ровно 720 минут разрешено. */
export const ICS_MAX_DURATION_MINUTES = 720

const DAY_MS = 24 * 60 * 60 * 1000
const MINUTE_MS = 60 * 1000
/** Допуск «ещё не прошло»: сервер не принимает начало раньше `now − 5 минут`. */
const PAST_TOLERANCE_MS = 5 * MINUTE_MS

// ---------------------------------------------------------------------------
// Типы ical.js, выведенные из типа модуля: значения импортируются динамически,
// а типы нужны на уровне модуля.
// ---------------------------------------------------------------------------

type IcalModule = typeof import('ical.js').default
type IcalComponent = InstanceType<IcalModule['Component']>
type IcalEvent = InstanceType<IcalModule['Event']>
type IcalProperty = InstanceType<IcalModule['Property']>
type IcalRecur = InstanceType<IcalModule['Recur']>
type IcalTime = InstanceType<IcalModule['Time']>
type IcalTimezone = InstanceType<IcalModule['Timezone']>

type ParseContext = {
  windowStart: number
  windowEnd: number
  /** Пояс основного салона; `null`, если салонов нет. */
  salonTimeZone: string | null
}

/** Разрешённый пояс, в котором трактуются настенные компоненты времени. */
type ZoneInfo =
  | { kind: 'utc' }
  | { kind: 'registered'; zone: IcalTimezone }
  | { kind: 'iana'; tzid: string }
  | { kind: 'floating'; tzid: string }

type TimeFailure = { failure: 'unknownTimeZone' | 'invalid' }

type ResolvedTime = {
  instantMs: number
  sourceTimeZone: string | null
  zone: ZoneInfo
}

type TimeResolution = ResolvedTime | TimeFailure

type WallDate = { year: number; month: number; day: number }

type WallClock = WallDate & {
  hour: number
  minute: number
  second: number
}

/** Всё, что нужно для одного вхождения, независимо от источника. */
type CandidateSource = {
  uid: string
  recurrenceId: string | null
  isRecurring: boolean
  /** Компонент, из которого читаются `SUMMARY`/`DESCRIPTION`/`STATUS`/`TRANSP`. */
  component: IcalComponent
  start: IcalTime | null
  startProperty: IcalProperty | null
  end: IcalTime | null
  endProperty: IcalProperty | null
}

type Candidate =
  | { kind: 'occurrence'; occurrence: IcsOccurrence }
  | { kind: 'past'; startAt: string }
  | { kind: 'outside'; startAt: string }
  | { kind: 'skipped' }

type OccurrenceSink = {
  occurrences: IcsOccurrence[]
  /** Ключи `(uid, recurrenceId)` уже отданных вхождений. */
  emitted: Set<string>
  /** Достигнут предел вхождений в окне: результат заведомо неполный. */
  tooMany: boolean
}

type EventGroup = {
  uid: string
  master: IcalComponent | null
  overrides: IcalComponent[]
}

// ---------------------------------------------------------------------------
// Кеши `Intl`: пояс и форматтер проверяются один раз на файл и на процесс.
// ---------------------------------------------------------------------------

const timeZoneValidity = new Map<string, boolean>()
const wallFormatters = new Map<string, Intl.DateTimeFormat | null>()

function isValidTimeZone(tzid: string): boolean {
  const cached = timeZoneValidity.get(tzid)

  if (cached !== undefined) {
    return cached
  }

  let valid = false

  try {
    // Конструктор бросает на неизвестном поясе — это и есть проверка IANA.
    new Intl.DateTimeFormat('en-CA', { timeZone: tzid })
    valid = true
  } catch {
    valid = false
  }

  timeZoneValidity.set(tzid, valid)
  return valid
}

function resolveWallFormatter(tzid: string): Intl.DateTimeFormat | null {
  const cached = wallFormatters.get(tzid)

  if (cached !== undefined) {
    return cached
  }

  let formatter: Intl.DateTimeFormat | null = null

  try {
    formatter = new Intl.DateTimeFormat('en-CA', {
      timeZone: tzid,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    })
  } catch {
    formatter = null
  }

  wallFormatters.set(tzid, formatter)
  return formatter
}

// ---------------------------------------------------------------------------
// Мелкие помощники.
// ---------------------------------------------------------------------------

function fail(
  failure: Exclude<IcsParseFailure, null>,
  skipped: IcsSkippedEvent[] = []
): IcsParseResult {
  return { occurrences: [], skipped, failure }
}

function skip(
  reason: IcsSkippedReason,
  summary: string,
  startAt: string | null
): IcsSkippedEvent {
  return { reason, summary, startAt }
}

function isoOf(instantMs: number): string {
  return new Date(instantMs).toISOString()
}

/** Ведущий `/` в `TZID` встречается у Outlook и старых экспортов. */
function canonicalTzid(value: string): string {
  const trimmed = value.trim()

  return trimmed.startsWith('/') ? trimmed.slice(1) : trimmed
}

function readTzidParam(property: IcalProperty | null): string | null {
  if (property === null) {
    return null
  }

  // В типах `getParameter` обещает `string | any[]`, но на отсутствующем
  // параметре ical.js возвращает `undefined`.
  const raw: unknown = property.getParameter('tzid')

  if (typeof raw === 'string') {
    const tzid = canonicalTzid(raw)
    return tzid === '' ? null : tzid
  }

  if (Array.isArray(raw)) {
    for (const item of raw) {
      if (typeof item === 'string') {
        const tzid = canonicalTzid(item)
        if (tzid !== '') {
          return tzid
        }
      }
    }
  }

  return null
}

function readText(component: IcalComponent, name: string): string {
  const value = component.getFirstPropertyValue(name)

  return typeof value === 'string' ? value : ''
}

/**
 * `CATEGORIES` может быть несколько и с несколькими значениями через запятую:
 * для матчеров склеиваем значения первой свойства, чтобы не потерять вторую
 * категорию (`HAIRCUT,BEARD`).
 */
function readCategories(component: IcalComponent): string {
  const property = component.getFirstProperty('categories')

  if (property === null) {
    return ''
  }

  const values: string[] = []

  for (const value of property.getValues()) {
    if (typeof value === 'string') {
      values.push(value)
    }
  }

  return values.join(',')
}

function readSequence(component: IcalComponent): number {
  const property = component.getFirstProperty('sequence')

  if (property === null) {
    return 0
  }

  const value: unknown = property.getFirstValue()

  if (typeof value === 'number' && Number.isFinite(value)) {
    return Math.trunc(value)
  }

  if (typeof value === 'string') {
    const parsed = Number.parseInt(value, 10)
    return Number.isFinite(parsed) ? parsed : 0
  }

  return 0
}

function readStatus(component: IcalComponent): string {
  return readText(component, 'status').trim().toUpperCase()
}

/** Значение свойства-даты; `null`, если свойства нет или это не `ICAL.Time`. */
function readTime(
  ical: IcalModule,
  property: IcalProperty | null
): IcalTime | null {
  if (property === null) {
    return null
  }

  const value = property.getFirstValue()

  return value instanceof ical.Time ? value : null
}

function readRecurrences(
  ical: IcalModule,
  component: IcalComponent
): IcalRecur[] {
  const result: IcalRecur[] = []

  for (const property of component.getAllProperties('rrule')) {
    const value = property.getFirstValue()

    if (value instanceof ical.Recur) {
      result.push(value)
    }
  }

  return result
}

function pad(value: number, length: number): string {
  return String(value).padStart(length, '0')
}

/** Момент из настенных компонентов, прочитанных как UTC (без ``Date.UTC``-квирка для лет < 100). */
function utcInstant(time: {
  year: number
  month: number
  day: number
  hour: number
  minute: number
  second: number
}): number {
  const date = new Date(0)
  date.setUTCFullYear(time.year, time.month - 1, time.day)
  date.setUTCHours(time.hour, time.minute, time.second, 0)

  return date.getTime()
}

/**
 * Настенные компоненты в IANA-поясе → UTC-момент.
 *
 * `salonDateTimeToUtc` работает с точностью до минуты; секунды добавляем после,
 * потому что смещение пояса внутри минуты не меняется. `null` — дыра перехода
 * на летнее время (такого времени в поясе нет) или невалидная дата.
 */
function wallClockInstant(
  time: IcalTime,
  tzid: string
): number | null {
  const date = `${pad(time.year, 4)}-${pad(time.month, 2)}-${pad(time.day, 2)}`
  const clock = `${pad(time.hour, 2)}:${pad(time.minute, 2)}`
  const iso = salonDateTimeToUtc(date, clock, tzid)

  if (iso === null) {
    return null
  }

  const second = Math.min(Math.max(time.second, 0), 59)
  return Date.parse(iso) + second * 1000
}

/** Настенные компоненты в уже разрешённом поясе → UTC-момент. */
function instantInZone(
  ical: IcalModule,
  time: IcalTime,
  zone: ZoneInfo
): number | null {
  if (zone.kind === 'utc') {
    return utcInstant(time)
  }

  if (zone.kind === 'registered') {
    try {
      const zoned = ical.Time.fromData(
        {
          year: time.year,
          month: time.month,
          day: time.day,
          hour: time.hour,
          minute: time.minute,
          second: time.second,
          isDate: false,
        },
        zone.zone
      )
      const instantMs = zoned.toJSDate().getTime()

      return Number.isFinite(instantMs) ? instantMs : null
    } catch {
      return null
    }
  }

  return wallClockInstant(time, zone.tzid)
}

/**
 * Правило пояса для одного `ICAL.Time`.
 *
 * `time.zone.tzid === 'UTC'` — `Z`-время. Разрешённый пояс (`floating`/`local` —
 * не разрешён) означает, что `ical.js` нашёл `VTIMEZONE`: `toJSDate()` корректен.
 * `TZID` без `VTIMEZONE` даёт floating-время, поэтому момент строится из
 * компонентов в IANA-поясе. Без `TZID` — floating во времени основного салона.
 */
function resolveTime(
  context: ParseContext,
  property: IcalProperty | null,
  time: IcalTime
): TimeResolution {
  const zoneTzid = time.zone.tzid

  if (zoneTzid === 'UTC') {
    const instantMs = time.toJSDate().getTime()

    if (!Number.isFinite(instantMs)) {
      return { failure: 'invalid' }
    }

    return { instantMs, sourceTimeZone: 'UTC', zone: { kind: 'utc' } }
  }

  const tzidParam = readTzidParam(property)
  const zoneResolved = zoneTzid !== 'floating' && zoneTzid !== 'local'

  if (zoneResolved) {
    const instantMs = time.toJSDate().getTime()

    if (!Number.isFinite(instantMs)) {
      return { failure: 'invalid' }
    }

    return {
      instantMs,
      sourceTimeZone: tzidParam ?? canonicalTzid(zoneTzid),
      zone: { kind: 'registered', zone: time.zone },
    }
  }

  if (tzidParam !== null) {
    if (!isValidTimeZone(tzidParam)) {
      return { failure: 'unknownTimeZone' }
    }

    const instantMs = wallClockInstant(time, tzidParam)

    if (instantMs === null) {
      return { failure: 'invalid' }
    }

    return {
      instantMs,
      sourceTimeZone: tzidParam,
      zone: { kind: 'iana', tzid: tzidParam },
    }
  }

  if (context.salonTimeZone === null) {
    return { failure: 'unknownTimeZone' }
  }

  if (!isValidTimeZone(context.salonTimeZone)) {
    return { failure: 'unknownTimeZone' }
  }

  const instantMs = wallClockInstant(time, context.salonTimeZone)

  if (instantMs === null) {
    return { failure: 'invalid' }
  }

  return {
    instantMs,
    sourceTimeZone: null,
    zone: { kind: 'floating', tzid: context.salonTimeZone },
  }
}

/**
 * Стабильная строка `RECURRENCE-ID`: UTC ISO, если пояс вхождения известен,
 * иначе `toICALString()`. Floating-время намеренно не пересчитывается в пояс
 * салона: идентичность вхождения не должна зависеть от выбранного салона.
 */
function recurrenceIdOf(
  property: IcalProperty | null,
  time: IcalTime
): string {
  if (time.isDate) {
    return time.toICALString()
  }

  const zoneTzid = time.zone.tzid

  if (zoneTzid !== 'floating' && zoneTzid !== 'local') {
    const instantMs = time.toJSDate().getTime()
    return Number.isFinite(instantMs)
      ? isoOf(instantMs)
      : time.toICALString()
  }

  const tzid = readTzidParam(property)

  if (tzid !== null && isValidTimeZone(tzid)) {
    const instantMs = wallClockInstant(time, tzid)

    if (instantMs !== null) {
      return isoOf(instantMs)
    }
  }

  return time.toICALString()
}

/** Ключ для сопоставления override с вхождением так, как это делает ical.js. */
function utcKeyOf(ical: IcalModule, time: IcalTime): string | null {
  try {
    return time.convertToZone(ical.Timezone.utcTimezone).toString()
  } catch {
    return null
  }
}

function recurrenceIdRaw(component: IcalComponent): string | null {
  const value = component.getFirstPropertyValue('recurrence-id')

  return value === null ? null : String(value)
}

// ---------------------------------------------------------------------------
// Настенная дата окна и сдвиг старта серии.
// ---------------------------------------------------------------------------

/**
 * Настенные компоненты момента в поясе вхождения.
 *
 * Для UTC/зарегистрированного пояса — `convertToZone`; для IANA без
 * `VTIMEZONE` и floating — `Intl`, потому что `toJSDate()` применил бы пояс
 * браузера.
 */
function wallClockInZone(
  ical: IcalModule,
  instantMs: number,
  zone: ZoneInfo
): WallClock | null {
  if (zone.kind === 'utc' || zone.kind === 'registered') {
    try {
      const source = ical.Time.fromJSDate(new Date(instantMs), true)
      const zoned = source.convertToZone(
        zone.kind === 'utc' ? ical.Timezone.utcTimezone : zone.zone
      )

      return {
        year: zoned.year,
        month: zoned.month,
        day: zoned.day,
        hour: zoned.hour,
        minute: zoned.minute,
        second: zoned.second,
      }
    } catch {
      return null
    }
  }

  const formatter = resolveWallFormatter(zone.tzid)

  if (formatter === null) {
    return null
  }

  try {
    const parts = formatter.formatToParts(new Date(instantMs))
    const values = new Map<string, number>()

    for (const part of parts) {
      if (part.type !== 'literal') {
        values.set(part.type, Number(part.value))
      }
    }

    const year = values.get('year')
    const month = values.get('month')
    const day = values.get('day')
    const hour = values.get('hour')
    const minute = values.get('minute')
    const second = values.get('second')

    if (
      year === undefined
      || month === undefined
      || day === undefined
      || hour === undefined
      || minute === undefined
      || second === undefined
    ) {
      return null
    }

    return { year, month, day, hour, minute, second }
  } catch {
    return null
  }
}

/** Настенные дата и время как число: сравнивать компоненты в одной зоне. */
function wallStamp(
  date: WallDate,
  hour = 0,
  minute = 0,
  second = 0
): number {
  return utcInstant({ ...date, hour, minute, second })
}

function addDaysToWallDate(date: WallDate, days: number): WallDate {
  const probe = new Date(0)
  probe.setUTCFullYear(date.year, date.month - 1, date.day)
  probe.setUTCDate(probe.getUTCDate() + days)

  return {
    year: probe.getUTCFullYear(),
    month: probe.getUTCMonth() + 1,
    day: probe.getUTCDate(),
  }
}

function wallWeekday(date: WallDate): number {
  const probe = new Date(0)
  probe.setUTCFullYear(date.year, date.month - 1, date.day)

  return probe.getUTCDay()
}

function daysInMonth(year: number, month: number): number {
  const probe = new Date(0)
  probe.setUTCFullYear(year, month, 0)

  return probe.getUTCDate()
}

function nextMonth(date: WallDate): WallDate {
  return date.month === 12
    ? { year: date.year + 1, month: 1, day: 1 }
    : { year: date.year, month: date.month + 1, day: 1 }
}

/**
 * Настенная дата сдвинутого `DTSTART` для бесконечной серии `INTERVAL = 1`.
 *
 * Зона и время суток остаются от настоящего `DTSTART`; меняется только дата,
 * чтобы итератор начал рядом с окном и при этом сохранил фазу правила.
 */
function shiftedStartDate(
  rule: IcalRecur,
  window: WallClock,
  original: IcalTime
): WallDate | null {
  const parts = rule.parts
  const windowDate: WallDate = {
    year: window.year,
    month: window.month,
    day: window.day,
  }
  const originalDate: WallDate = {
    year: original.year,
    month: original.month,
    day: original.day,
  }
  const hasDayParts =
    parts.BYMONTHDAY !== undefined || parts.BYDAY !== undefined

  if (rule.freq === 'WEEKLY') {
    if (parts.BYDAY !== undefined) {
      // Дни недели заданы правилом: фаза не зависит от даты старта.
      return windowDate
    }

    // Без BYDAY фаза — день недели DTSTART: доводим дату окна до него.
    const shift = (wallWeekday(originalDate) - wallWeekday(windowDate) + 7) % 7
    return addDaysToWallDate(windowDate, shift)
  }

  if (rule.freq === 'MONTHLY') {
    if (hasDayParts) {
      return windowDate
    }

    // День месяца наследуется от DTSTART: берём ближайший месяц, где он есть,
    // и где само вхождение ещё не прошло.
    let candidate: WallDate = windowDate

    for (let attempt = 0; attempt < 36; attempt += 1) {
      if (originalDate.day <= daysInMonth(candidate.year, candidate.month)) {
        const date: WallDate = {
          year: candidate.year,
          month: candidate.month,
          day: originalDate.day,
        }

        if (
          wallStamp(
            date,
            original.hour,
            original.minute,
            original.second
          ) >= wallStamp(window)
        ) {
          return date
        }
      }

      candidate = nextMonth(candidate)
    }

    return null
  }

  if (rule.freq === 'YEARLY') {
    if (parts.BYDAY !== undefined) {
      // Дни задаёт BYDAY — дата старта влияет только на год.
      return windowDate
    }

    if (parts.BYMONTH !== undefined) {
      // Месяц задаёт BYMONTH; день — BYMONTHDAY или день DTSTART.
      const day = hasDayParts ? 1 : originalDate.day
      let candidate: WallDate = windowDate

      for (let attempt = 0; attempt < 13; attempt += 1) {
        if (day <= daysInMonth(candidate.year, candidate.month)) {
          return { year: candidate.year, month: candidate.month, day }
        }

        candidate = nextMonth(candidate)
      }

      return null
    }

    // Месяц наследуется от DTSTART (в том числе при BYMONTHDAY): год — окно.
    const day = hasDayParts ? 1 : originalDate.day
    let year = window.year

    for (let attempt = 0; attempt < 400; attempt += 1) {
      if (day <= daysInMonth(year, originalDate.month)) {
        const date: WallDate = { year, month: originalDate.month, day }

        if (
          hasDayParts
          || wallStamp(
            date,
            original.hour,
            original.minute,
            original.second
          ) >= wallStamp(window)
        ) {
          return date
        }
      }

      year += 1
    }

    return null
  }

  // DAILY, HOURLY, MINUTELY, SECONDLY: время суток сохраняем, дата — окно.
  return windowDate
}

/**
 * Стартовое время итератора серии.
 *
 * `bounded`/`INTERVAL > 1` — от настоящего `DTSTART`; иначе — сдвинутая копия
 * с сохранением зоны и времени суток. Если сдвиг ушёл бы раньше настоящего
 * `DTSTART` (серия начинается внутри окна или в будущем), берём `DTSTART`:
 * раскрывать вхождения до начала серии нельзя.
 */
function chooseIterationStart(
  ical: IcalModule,
  context: ParseContext,
  event: IcalEvent,
  master: IcalComponent,
  recurrences: readonly IcalRecur[],
  startZone: ZoneInfo
): IcalTime {
  const original = event.startDate

  if (recurrences.length === 0) {
    return original
  }

  const bounded =
    master.hasProperty('rdate')
    || recurrences.some(
      (rule) => rule.count !== null || rule.until !== null
    )
  const phased = recurrences.some((rule) => rule.interval > 1)

  // Несколько правил на одном событии ical.js разворачивает от одного
  // `DTSTART`; сдвиг по первому правилу сломал бы фазу остальных.
  if (bounded || phased || recurrences.length > 1) {
    return original
  }

  const windowWall = wallClockInZone(ical, context.windowStart, startZone)

  if (windowWall === null) {
    return original
  }

  const shiftedDate = shiftedStartDate(recurrences[0], windowWall, original)

  if (shiftedDate === null) {
    return original
  }

  const originalDate: WallDate = {
    year: original.year,
    month: original.month,
    day: original.day,
  }

  if (wallStamp(shiftedDate) < wallStamp(originalDate)) {
    return original
  }

  const shifted = original.clone()
  shifted.year = shiftedDate.year
  shifted.month = shiftedDate.month
  shifted.day = shiftedDate.day

  return shifted
}

// ---------------------------------------------------------------------------
// Вхождения: отбор в окно, длительность, тексты.
// ---------------------------------------------------------------------------

function resolveEnd(
  ical: IcalModule,
  context: ParseContext,
  source: CandidateSource,
  startZone: ZoneInfo
): number | TimeFailure {
  const end = source.end

  if (end === null) {
    return { failure: 'invalid' }
  }

  const endTzid = readTzidParam(source.endProperty)
  const endZoneTzid = end.zone.tzid
  const ownZone =
    endTzid !== null
    || (endZoneTzid !== 'floating' && endZoneTzid !== 'local')

  if (source.endProperty !== null && ownZone) {
    const resolution = resolveTime(
      context,
      source.endProperty,
      end
    )

    return 'failure' in resolution ? resolution : resolution.instantMs
  }

  // `DTEND` без `TZID` (и конец из `DURATION`) наследует пояс начала.
  const instantMs = instantInZone(ical, end, startZone)

  return instantMs === null ? { failure: 'invalid' } : instantMs
}

/**
 * Один кандидат в вхождение.
 *
 * Причины `past`/`outsidePeriod` не пишутся в `skipped` здесь: серия не должна
 * копить прошедшие вхождения, а одиночное событие добавляет их вызывающий.
 */
function collectCandidate(
  ical: IcalModule,
  context: ParseContext,
  source: CandidateSource,
  skipped: IcsSkippedEvent[]
): Candidate {
  const summary = readText(source.component, 'summary')

  if (source.start === null) {
    skipped.push(skip('invalid', summary, null))
    return { kind: 'skipped' }
  }

  if (source.start.isDate) {
    skipped.push(skip('allDay', summary, null))
    return { kind: 'skipped' }
  }

  const startResolution = resolveTime(
    context,
    source.startProperty,
    source.start
  )

  if ('failure' in startResolution) {
    skipped.push(skip(startResolution.failure, summary, null))
    return { kind: 'skipped' }
  }

  const startMs = startResolution.instantMs

  if (startMs < context.windowStart) {
    return { kind: 'past', startAt: isoOf(startMs) }
  }

  if (startMs >= context.windowEnd) {
    return { kind: 'outside', startAt: isoOf(startMs) }
  }

  if (readStatus(source.component) === 'CANCELLED') {
    skipped.push(skip('cancelled', summary, isoOf(startMs)))
    return { kind: 'skipped' }
  }

  let endAt: string | null = null
  let durationMinutes: number | null = null

  if (source.end !== null) {
    const endResolution = resolveEnd(
      ical,
      context,
      source,
      startResolution.zone
    )

    if (typeof endResolution !== 'number') {
      skipped.push(skip(endResolution.failure, summary, isoOf(startMs)))
      return { kind: 'skipped' }
    }

    if (endResolution > startMs) {
      const minutes = (endResolution - startMs) / MINUTE_MS

      if (minutes > ICS_MAX_DURATION_MINUTES) {
        skipped.push(skip('tooLong', summary, isoOf(startMs)))
        return { kind: 'skipped' }
      }

      endAt = isoOf(endResolution)
      durationMinutes = Math.max(1, Math.round(minutes))
    }
  }

  return {
    kind: 'occurrence',
    occurrence: {
      uid: source.uid,
      recurrenceId: source.recurrenceId,
      summary,
      description: readText(source.component, 'description'),
      location: readText(source.component, 'location'),
      categories: readCategories(source.component),
      startAt: isoOf(startMs),
      endAt,
      durationMinutes,
      sourceTimeZone: startResolution.sourceTimeZone,
      isRecurring: source.isRecurring,
      status: readStatus(source.component) === 'TENTATIVE'
        ? 'tentative'
        : 'confirmed',
      isFree:
        readText(source.component, 'transp').trim().toUpperCase()
        === 'TRANSPARENT',
      sequence: readSequence(source.component),
    },
  }
}

function addOccurrence(
  sink: OccurrenceSink,
  occurrence: IcsOccurrence
): void {
  if (sink.tooMany) {
    return
  }

  const key = `${occurrence.uid}\u0000${occurrence.recurrenceId ?? ''}`

  if (sink.emitted.has(key)) {
    return
  }

  if (sink.occurrences.length >= ICS_MAX_OCCURRENCES) {
    sink.tooMany = true
    return
  }

  sink.emitted.add(key)
  sink.occurrences.push(occurrence)
}

/** `startAt` для записей о пропуске: момент, если он вообще разрешается. */
function bestEffortStartAt(
  ical: IcalModule,
  context: ParseContext,
  component: IcalComponent
): string | null {
  const property = component.getFirstProperty('dtstart')
  const time = readTime(ical, property)

  if (time === null || time.isDate) {
    return null
  }

  const resolution = resolveTime(context, property, time)

  return 'failure' in resolution ? null : isoOf(resolution.instantMs)
}

function compareOccurrences(
  left: IcsOccurrence,
  right: IcsOccurrence
): number {
  if (left.startAt !== right.startAt) {
    return left.startAt < right.startAt ? -1 : 1
  }

  if (left.uid !== right.uid) {
    return left.uid < right.uid ? -1 : 1
  }

  if (left.recurrenceId === right.recurrenceId) {
    return 0
  }

  if (left.recurrenceId === null) {
    return 1
  }

  if (right.recurrenceId === null) {
    return -1
  }

  return left.recurrenceId < right.recurrenceId ? -1 : 1
}

// ---------------------------------------------------------------------------
// Группировка VEVENT по UID.
// ---------------------------------------------------------------------------

function groupComponents(
  components: readonly IcalComponent[],
  skipped: IcsSkippedEvent[],
  ical: IcalModule,
  context: ParseContext
): EventGroup[] {
  const groups = new Map<string, EventGroup>()

  for (const component of components) {
    const uid = readText(component, 'uid').trim()

    if (uid === '') {
      skipped.push(
        skip(
          'noUid',
          readText(component, 'summary'),
          bestEffortStartAt(ical, context, component)
        )
      )
      continue
    }

    let group = groups.get(uid)

    if (group === undefined) {
      group = { uid, master: null, overrides: [] }
      groups.set(uid, group)
    }

    if (component.hasProperty('recurrence-id')) {
      group.overrides.push(component)
      continue
    }

    // Несколько мастеров одного UID — это правки одного события: побеждает
    // наибольший SEQUENCE, при равенстве — первый.
    if (
      group.master === null
      || readSequence(component) > readSequence(group.master)
    ) {
      group.master = component
    }
  }

  return [...groups.values()]
}

/** Override одного `RECURRENCE-ID` с наибольшим `SEQUENCE`; первый при равенстве. */
function dedupeOverrides(
  overrides: readonly IcalComponent[]
): IcalComponent[] {
  const chosen = new Map<string, IcalComponent>()
  let anonymous = 0

  for (const override of overrides) {
    const raw = recurrenceIdRaw(override)
    const key = raw ?? `\u0000missing-${anonymous++}`
    const existing = chosen.get(key)

    if (
      existing === undefined
      || readSequence(override) > readSequence(existing)
    ) {
      chosen.set(key, override)
    }
  }

  return [...chosen.values()]
}

type RangeEvent = {
  start: IcalTime
  end: IcalTime
}

/**
 * Начало и конец компонента через `Event` (учитывает `DURATION`).
 *
 * `exceptions: []` обязателен: без него конструктор `Event` сам привяжет все
 * `VEVENT` с `RECURRENCE-ID` из родительского календаря — в том числе чужих
 * `UID`, и подменил бы событие случайным override.
 */
function rangeOf(
  ical: IcalModule,
  component: IcalComponent
): RangeEvent | null {
  const start = readTime(ical, component.getFirstProperty('dtstart'))

  if (start === null) {
    return null
  }

  const source = new ical.Event(component, { exceptions: [] })
  const end: IcalTime | null = source.endDate

  if (end === null) {
    return null
  }

  return { start, end }
}

// ---------------------------------------------------------------------------
// Разбор календаря.
// ---------------------------------------------------------------------------

/**
 * Одиночное событие (в том числе override без мастера): одна запись без
 * раскрытия. `details` может подменить значения, если это override вхождения.
 */
function processSingle(
  ical: IcalModule,
  context: ParseContext,
  sink: OccurrenceSink,
  skipped: IcsSkippedEvent[],
  source: CandidateSource
): void {
  const candidate = collectCandidate(ical, context, source, skipped)

  if (candidate.kind === 'occurrence') {
    addOccurrence(sink, candidate.occurrence)
    return
  }

  if (candidate.kind === 'past' || candidate.kind === 'outside') {
    skipped.push(
      skip(
        candidate.kind === 'past' ? 'past' : 'outsidePeriod',
        readText(source.component, 'summary'),
        candidate.startAt
      )
    )
  }
}

/**
 * Override, который итератор не вернул: `RECURRENCE-ID` раньше окна, а
 * перенесённый `DTSTART` — внутри. Терять такой визит нельзя, поэтому
 * проверяем каждый override явно и дедублируем по `(uid, recurrenceId)`.
 */
function appendMovedOverrides(
  ical: IcalModule,
  context: ParseContext,
  sink: OccurrenceSink,
  skipped: IcsSkippedEvent[],
  group: EventGroup,
  overrides: readonly IcalComponent[],
  handledRaw: ReadonlySet<string>,
  handledStable: ReadonlySet<string>
): void {
  for (const override of overrides) {
    const recurrenceProperty = override.getFirstProperty('recurrence-id')
    const recurrenceTime = readTime(ical, recurrenceProperty)
    const stable =
      recurrenceTime === null
        ? recurrenceIdRaw(override) ?? ''
        : recurrenceIdOf(recurrenceProperty, recurrenceTime)
    const raw = recurrenceIdRaw(override)
    const utcKey = recurrenceTime === null ? null : utcKeyOf(ical, recurrenceTime)

    if (
      (raw !== null && handledRaw.has(raw))
      || (utcKey !== null && handledRaw.has(utcKey))
      || handledStable.has(stable)
    ) {
      continue
    }

    if (recurrenceTime === null) {
      continue
    }

    const range = rangeOf(ical, override)

    if (range === null) {
      // У отмены без DTSTART нет момента: её обработает итератор, если
      // вхождение попадает в окно.
      continue
    }

    const candidate = collectCandidate(
      ical,
      context,
      {
        uid: group.uid,
        recurrenceId: stable,
        isRecurring: group.master !== null,
        component: override,
        start: range.start,
        startProperty: override.getFirstProperty('dtstart'),
        end: range.end,
        endProperty: override.getFirstProperty('dtend'),
      },
      skipped
    )

    if (candidate.kind === 'occurrence') {
      addOccurrence(sink, candidate.occurrence)
    }
  }
}

/**
 * Итератор override, у которого нет `DTSTART` (так приходит отмена из iTIP):
 * `getOccurrenceDetails` на нём бросает, поэтому причину берём из компонента.
 */
function findFailedOverride(
  ical: IcalModule,
  overrides: readonly IcalComponent[],
  next: IcalTime
): IcalComponent | null {
  const raw = next.toString()
  const utcKey = utcKeyOf(ical, next)

  for (const override of overrides) {
    const recurrenceTime = readTime(
      ical,
      override.getFirstProperty('recurrence-id')
    )

    if (recurrenceTime === null) {
      continue
    }

    if (
      recurrenceTime.toString() === raw
      || (utcKey !== null && utcKeyOf(ical, recurrenceTime) === utcKey)
    ) {
      return override
    }
  }

  return null
}

function expandSeries(
  ical: IcalModule,
  context: ParseContext,
  sink: OccurrenceSink,
  skipped: IcsSkippedEvent[],
  group: EventGroup,
  master: IcalComponent,
  overrides: readonly IcalComponent[],
  recurrences: readonly IcalRecur[],
  startZone: ZoneInfo
): void {
  const event = new ical.Event(master, { exceptions: [...overrides] })
  const masterProperty = master.getFirstProperty('dtstart')
  const iteratorStart = chooseIterationStart(
    ical,
    context,
    event,
    master,
    recurrences,
    startZone
  )
  const iterator = event.iterator(iteratorStart)
  const handledRaw = new Set<string>()
  const handledStable = new Set<string>()
  let processed = 0
  let finished = false

  while (processed < ICS_MAX_ITERATIONS_PER_SERIES) {
    let next: IcalTime | null | undefined

    try {
      next = iterator.next()
    } catch {
      // Битое правило не должно ронять весь файл: серия остаётся нераскрытой.
      skipped.push(skip('invalid', readText(master, 'summary'), null))
      return
    }

    // `RecurExpansion.next()` возвращает `undefined`, хотя типы обещают `Time`.
    if (next === null || next === undefined) {
      finished = true
      break
    }

    processed += 1

    // Останавливаемся по времени самого `RECURRENCE-ID`, а не по началу
    // вхождения: override мог перенести это вхождение за окно, и тогда
    // следующее (в окне) терять нельзя. Перенесённые в окно более поздние
    // вхождения добирает `appendMovedOverrides`.
    const recurrenceInstant = resolveTime(context, masterProperty, next)

    if (
      !('failure' in recurrenceInstant)
      && recurrenceInstant.instantMs >= context.windowEnd
    ) {
      finished = true
      break
    }

    const raw = next.toString()
    const utcKey = utcKeyOf(ical, next)
    const recurrenceId = recurrenceIdOf(masterProperty, next)

    handledRaw.add(raw)
    if (utcKey !== null) {
      handledRaw.add(utcKey)
    }
    handledStable.add(recurrenceId)

    let item: IcalComponent
    let start: IcalTime | null
    let end: IcalTime | null

    try {
      const details = event.getOccurrenceDetails(next)
      item = details.item.component
      start = details.startDate instanceof ical.Time
        ? details.startDate
        : null
      end = details.endDate instanceof ical.Time ? details.endDate : null
    } catch {
      const override = findFailedOverride(ical, overrides, next)

      if (override !== null && readStatus(override) === 'CANCELLED') {
        skipped.push(
          skip('cancelled', readText(override, 'summary'), null)
        )
      } else {
        skipped.push(
          skip(
            'invalid',
            override === null ? readText(master, 'summary') : readText(override, 'summary'),
            null
          )
        )
      }

      continue
    }

    const candidate = collectCandidate(
      ical,
      context,
      {
        uid: group.uid,
        recurrenceId,
        isRecurring: true,
        component: item,
        start,
        startProperty:
          item.getFirstProperty('dtstart') ?? masterProperty,
        end,
        endProperty: item.getFirstProperty('dtend'),
      },
      skipped
    )

    if (candidate.kind === 'occurrence') {
      addOccurrence(sink, candidate.occurrence)
    } else if (candidate.kind === 'past') {
      skipped.push(
        skip('past', readText(item, 'summary'), candidate.startAt)
      )
    }
    // `outside` здесь — перенесённое за окно вхождение: серия продолжается,
    // поэтому просто идём дальше, а не обрываем раскрытие.

    if (sink.tooMany) {
      return
    }
  }

  if (!finished) {
    // Лимит шагов: проверяем, остались ли ещё вхождения. Если да — результат
    // неполный, и молча обрезать его нельзя.
    let probe: IcalTime | null | undefined = null

    try {
      probe = iterator.next()
    } catch {
      probe = null
    }

    if (probe !== null && probe !== undefined) {
      sink.tooMany = true
      return
    }
  }

  appendMovedOverrides(
    ical,
    context,
    sink,
    skipped,
    group,
    overrides,
    handledRaw,
    handledStable
  )
}

function parseCalendar(
  ical: IcalModule,
  context: ParseContext,
  components: readonly IcalComponent[],
  method: string,
  skipped: IcsSkippedEvent[]
): IcsParseResult {
  const sink: OccurrenceSink = {
    occurrences: [],
    emitted: new Set<string>(),
    tooMany: false,
  }
  const groups = groupComponents(components, skipped, ical, context)

  for (const group of groups) {
    const master = group.master

    if (method === 'CANCEL') {
      const source = master ?? group.overrides[0]
      skipped.push(
        skip(
          'cancelled',
          readText(source, 'summary'),
          bestEffortStartAt(ical, context, source)
        )
      )
      continue
    }

    if (master === null) {
      // Override без мастера: раскрывать нечего, но само событие может быть
      // валидным — отдаём его как одиночное вхождение.
      for (const override of dedupeOverrides(group.overrides)) {
        const range = rangeOf(ical, override)

        if (range === null) {
          skipped.push(skip('invalid', readText(override, 'summary'), null))
          continue
        }

        const recurrenceProperty = override.getFirstProperty('recurrence-id')
        const recurrenceTime = readTime(ical, recurrenceProperty)

        processSingle(ical, context, sink, skipped, {
          uid: group.uid,
          recurrenceId:
            recurrenceTime === null
              ? null
              : recurrenceIdOf(recurrenceProperty, recurrenceTime),
          isRecurring: false,
          component: override,
          start: range.start,
          startProperty: override.getFirstProperty('dtstart'),
          end: range.end,
          endProperty: override.getFirstProperty('dtend'),
        })

        if (sink.tooMany) {
          return fail('tooManyOccurrences', skipped)
        }
      }

      continue
    }

    const startProperty = master.getFirstProperty('dtstart')
    const start = readTime(ical, startProperty)

    if (start === null) {
      skipped.push(
        skip('invalid', readText(master, 'summary'), null)
      )
      continue
    }

    if (start.isDate) {
      skipped.push(skip('allDay', readText(master, 'summary'), null))
      continue
    }

    if (readStatus(master) === 'CANCELLED') {
      // Отмена мастера отменяет всю серию: переносы отдельных вхождений не
      // спасают её.
      skipped.push(
        skip(
          'cancelled',
          readText(master, 'summary'),
          bestEffortStartAt(ical, context, master)
        )
      )
      continue
    }

    const overrides = dedupeOverrides(group.overrides)
    const isRecurring =
      master.hasProperty('rrule') || master.hasProperty('rdate')

    if (!isRecurring) {
      const range = rangeOf(ical, master)

      if (range === null) {
        skipped.push(skip('invalid', readText(master, 'summary'), null))
        continue
      }

      processSingle(ical, context, sink, skipped, {
        uid: group.uid,
        // Одиночное невхождение серии: по контракту `recurrenceId === null`.
        recurrenceId: null,
        isRecurring: false,
        component: master,
        start: range.start,
        startProperty,
        end: range.end,
        endProperty: master.getFirstProperty('dtend'),
      })

      if (sink.tooMany) {
        return fail('tooManyOccurrences', skipped)
      }

      // Override одноразового события — редкость, но перенесённый визит не
      // должен потеряться.
      appendMovedOverrides(
        ical,
        context,
        sink,
        skipped,
        group,
        overrides,
        new Set<string>([start.toString()]),
        new Set<string>([recurrenceIdOf(startProperty, start)])
      )
      continue
    }

    const recurrences = readRecurrences(ical, master)
    const hasRule = master.hasProperty('rrule')

    if (hasRule && recurrences.length === 0) {
      // RRULE есть, но разобрать правило не удалось.
      skipped.push(skip('invalid', readText(master, 'summary'), null))
      continue
    }

    const startResolution = resolveTime(context, startProperty, start)

    if ('failure' in startResolution) {
      skipped.push(skip(startResolution.failure, readText(master, 'summary'), null))
      continue
    }

    if (!hasRule && master.hasProperty('rdate')) {
      // `RecurExpansion` для RDATE-серии не включает сам DTSTART (в наборе
      // только дополнительные даты), поэтому первое вхождение добавляем сами.
      const range = rangeOf(ical, master)

      if (range === null) {
        skipped.push(skip('invalid', readText(master, 'summary'), null))
        continue
      }

      processSingle(ical, context, sink, skipped, {
        uid: group.uid,
        recurrenceId: recurrenceIdOf(startProperty, start),
        isRecurring: true,
        component: master,
        start: range.start,
        startProperty,
        end: range.end,
        endProperty: master.getFirstProperty('dtend'),
      })

      if (sink.tooMany) {
        return fail('tooManyOccurrences', skipped)
      }
    }

    expandSeries(
      ical,
      context,
      sink,
      skipped,
      group,
      master,
      overrides,
      recurrences,
      startResolution.zone
    )

    if (sink.tooMany) {
      return fail('tooManyOccurrences', skipped)
    }
  }

  if (sink.occurrences.length === 0) {
    return { occurrences: [], skipped, failure: 'noEventsInPeriod' }
  }

  const occurrences = [...sink.occurrences]
  occurrences.sort(compareOccurrences)

  return { occurrences, skipped, failure: null }
}

// ---------------------------------------------------------------------------
// Публичная точка входа.
// ---------------------------------------------------------------------------

/**
 * Текст `.ics` → вхождения в окне, пропуски и код ошибки.
 *
 * Не бросает: битый файл, битые правила и неизвестные пояса становятся
 * `failure`/`skipped`. `ical.js` импортируется динамически, чтобы вставка
 * списка не тянула календарный чанк.
 */
export async function parseIcs(
  text: string,
  options: IcsParseOptions
): Promise<IcsParseResult> {
  let ical: IcalModule | null = null

  try {
    ical = (await import('ical.js')).default
  } catch {
    return fail('invalid')
  }

  if (ical === null) {
    return fail('invalid')
  }

  // Глобальный singleton: состояние предыдущего файла не должно влиять на этот.
  ical.TimezoneService.reset()

  try {
    const root = new ical.Component(ical.parse(text))

    if (root.name !== 'vcalendar') {
      return fail('invalid')
    }

    // Регистрация — строго до первого чтения дат: иначе `TZID` без
    // зарегистрированного `VTIMEZONE` станет floating-временем.
    for (const timezone of root.getAllSubcomponents('vtimezone')) {
      const tzidValue = timezone.getFirstPropertyValue('tzid')
      const tzid = typeof tzidValue === 'string'
        ? canonicalTzid(tzidValue)
        : ''

      if (tzid === '') {
        continue
      }

      try {
        const zone = new ical.Timezone({ component: timezone, tzid })
        ical.TimezoneService.register(zone)

        // Некоторые экспорты пишут `TZID=/Europe/Berlin` в `VTIMEZONE`, а в
        // событиях — без слэша (и наоборот): регистрируем оба написания.
        const rawValue = typeof tzidValue === 'string' ? tzidValue.trim() : ''
        if (rawValue !== '' && rawValue !== tzid) {
          ical.TimezoneService.register(zone, rawValue)
        }
      } catch {
        // Битый `VTIMEZONE` не должен отменять разбор остальных событий.
      }
    }

    const components = root.getAllSubcomponents('vevent')

    if (components.length === 0) {
      return fail('empty')
    }

    if (components.length > ICS_MAX_VEVENTS) {
      return fail('tooManyEvents')
    }

    const defaultSalon =
      options.salons.find(
        (salon) => salon.salonId === options.defaultSalonId
      ) ?? options.salons[0]

    const salonTimeZone = defaultSalon?.timeZoneId.trim() ?? ''
    const context: ParseContext = {
      windowStart: options.now - PAST_TOLERANCE_MS,
      windowEnd: options.now + options.periodDays * DAY_MS,
      salonTimeZone: salonTimeZone === '' ? null : salonTimeZone,
    }
    const method = readText(root, 'method').trim().toUpperCase()

    return parseCalendar(ical, context, components, method, [])
  } catch {
    // `ICAL.parse` и итераторы бросают на недоверенном вводе: наружу ошибка
    // не уходит, форма покажет «битый файл».
    return fail('invalid')
  } finally {
    // Следующий файл не должен видеть `VTIMEZONE` этого.
    ical.TimezoneService.reset()
  }
}
