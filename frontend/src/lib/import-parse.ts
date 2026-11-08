import { addDays } from '@/lib/barber-slots'
import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import {
  collapse,
  findPhone,
  findSalon,
  findService,
  flatten,
  isImportPhone,
  normalizeImportPhone,
  removeRange,
  splitName,
} from '@/lib/import-match'
import {
  salonDateFromInstant,
  salonDateTimeToUtc,
  salonTimeFromInstant,
} from '@/lib/salon-time'

/**
 * Разбор вставленного списка записей.
 *
 * Чистые функции без сети и React: строка текста на входе, черновики на выходе.
 * Порядок разбора важен — телефон ищется первым, иначе его цифры съедят время,
 * а «12.11» остаётся датой, хотя и похоже на время. Строка-заголовок даты
 * («12 ноября») задаёт дату следующим строкам: так выглядит журнал в Google Docs,
 * и это главный формат среза.
 */

export const IMPORT_MAX_ROWS = 50
export const IMPORT_MAX_TEXT_LENGTH = 10_000

/** Откуда пришла строка: вставленный список или файл календаря. */
export type ImportSource = 'text' | 'ics'

/**
 * Мягкая подсказка строки календаря: не ошибка, поэтому запись не блокирует.
 *
 * `personal` — похоже на личное событие, `free` — `TRANSP:TRANSPARENT`,
 * `tentative` — `STATUS:TENTATIVE`, `noContact` — нет ни телефона, ни услуги,
 * `salonFallback` — салон не найден и выбран основной, `durationMismatch` —
 * длительность события отличается от услуги, `timeZoneConverted` — время
 * пересчитано из другого пояса, `phoneFromDescription` — телефон найден в
 * описании, `nameTruncated`/`phoneTruncated` — значение обрезано до лимита.
 */
export type ImportHint =
  | 'personal'
  | 'free'
  | 'tentative'
  | 'noContact'
  | 'salonFallback'
  | 'durationMismatch'
  | 'timeZoneConverted'
  | 'phoneFromDescription'
  | 'nameTruncated'
  | 'phoneTruncated'

export type ImportIssue =
  | 'noDate'
  | 'badDate'
  | 'noTime'
  | 'badTime'
  | 'noName'
  | 'noPhone'
  | 'badPhone'
  | 'noService'
  | 'ambiguousService'
  | 'past'
  | 'duplicate'
  | 'overlap'
  | 'tooFar'

export type ImportDraft = {
  /** Номер строки в исходном тексте, с 1 — для подсказок и фокуса. */
  line: number
  raw: string
  /** Локальная дата салона `YYYY-MM-DD` или `null`. */
  date: string | null
  /** `HH:mm` или `null`. */
  time: string | null
  startAt: string | null
  endAt: string | null
  firstName: string | null
  lastName: string | null
  phone: string | null
  salonId: string | null
  /** `barberServiceId`. */
  serviceId: string | null
  /** Для выбора при неоднозначности. */
  serviceCandidates: string[]
  issues: ImportIssue[]
  /** Источник строки: `text` — вставленный список, `ics` — событие календаря. */
  source: ImportSource
  /** `UID` события календаря; `null` для текста. Основа ключа идемпотентности. */
  externalUid: string | null
  /** `RECURRENCE-ID` вхождения серии в стабильном виде; `null` для одиночного. */
  recurrenceId: string | null
  /** Исходный `SUMMARY` события — для подписи и отладки, не для заметки клиента. */
  eventSummary: string | null
  /** Абсолютный момент начала события в UTC: правка полей не должна терять инстант. */
  eventStartAt: string | null
  /** Абсолютный конец события; `null`, если в календаре его нет. */
  eventEndAt: string | null
  /** Длительность события из календаря; сохраняется при сдвиге времени. */
  eventDurationMinutes: number | null
  /** `true`, пока строка показывает исходное время события. */
  useEventTime: boolean
  /** `TZID` события, `'UTC'` или `null` для floating: нужен для подсказки о пересчёте. */
  sourceTimeZone: string | null
  /** Мягкие подсказки календаря: не ошибки, строку не блокируют. */
  hints: ImportHint[]
  /** Стартовое состояние галочки; у текста всегда `true`. */
  defaultIncluded: boolean
}

export type ImportParseOptions = {
  services: readonly BarberProfileService[]
  salons: readonly BarberProfileSalon[]
  /** `YYYY-MM-DD` в поясе салона по умолчанию. */
  today: string
  now: number
  /** Салон по умолчанию: primary, иначе первый. */
  defaultSalonId: string
}

export type ImportParseResult = {
  rows: ImportDraft[]
  /** `tooManyRows` — строк больше 50, `rows` пуст; `empty` — разбирать нечего. */
  failure: 'empty' | 'tooManyRows' | null
}

/** Допуск «ещё не прошло»: запись через пять минут создавать уже поздно. */
const PAST_TOLERANCE_MS = 5 * 60 * 1000

/**
 * Жёсткий серверный предел начала: запись дальше 90 дней не создаётся.
 *
 * Час запаса нужен потому, что окно разбора — `now + periodDays` эксклюзивно:
 * событие ровно на границе ещё попадает в файл, но создавать его уже нельзя.
 */
const FAR_TOLERANCE_MS = 90 * 24 * 60 * 60 * 1000 - 60 * 60 * 1000

/** Мягкий предел длительности импорта: длиннее — это уже не визит. */
const MAX_IMPORT_DURATION_MINUTES = 720

const LINE_BREAK = /\r\n|\r|\n/

/**
 * Русские месяцы: формы, которые встречаются в журнале.
 *
 * Именно слова, а не основы: «мар» — это март, а «Мария» начинается с тех же
 * букв, и поиск по началу превратил бы имя в дату.
 */
const MONTH_WORDS: ReadonlyMap<string, number> = new Map([
  ['январь', 1],
  ['января', 1],
  ['янв', 1],
  ['февраль', 2],
  ['февраля', 2],
  ['фев', 2],
  ['март', 3],
  ['марта', 3],
  ['мар', 3],
  ['апрель', 4],
  ['апреля', 4],
  ['апр', 4],
  ['май', 5],
  ['мая', 5],
  ['мае', 5],
  ['июнь', 6],
  ['июня', 6],
  ['июн', 6],
  ['июль', 7],
  ['июля', 7],
  ['июл', 7],
  ['август', 8],
  ['августа', 8],
  ['авг', 8],
  ['сентябрь', 9],
  ['сентября', 9],
  ['сент', 9],
  ['сен', 9],
  ['октябрь', 10],
  ['октября', 10],
  ['окт', 10],
  ['ноябрь', 11],
  ['ноября', 11],
  ['нояб', 11],
  ['ноя', 11],
  ['декабрь', 12],
  ['декабря', 12],
  ['дек', 12],
])

/** Дни недели: полные названия и сокращения. */
const WEEKDAY_STEMS: readonly (readonly [string, number])[] = [
  ['воскресенье', 0],
  ['воскресение', 0],
  ['понедельник', 1],
  ['вторник', 2],
  ['четверг', 4],
  ['пятница', 5],
  ['суббота', 6],
  ['среда', 3],
  ['пн', 1],
  ['вт', 2],
  ['ср', 3],
  ['чт', 4],
  ['пт', 5],
  ['сб', 6],
  ['вс', 0],
]

const RELATIVE_DAYS: readonly (readonly [string, number])[] = [
  ['послезавтра', 2],
  ['завтра', 1],
  ['сегодня', 0],
]

/** Слова шапки таблицы: строка из них — заголовок, а не запись. */
const HEADER_WORDS = new Set([
  'дата',
  'время',
  'клиент',
  'клиенты',
  'телефон',
  'услуга',
  'услуги',
  'салон',
  'цена',
  'имя',
  'фамилия',
  'день',
  'запись',
  'записи',
])

type DateHit = {
  start: number
  end: number
  /** `YYYY-MM-DD`; `null` — дата была, но такой даты нет (29 февраля невисокосного). */
  date: string | null
}

type TimeHit = {
  start: number
  end: number
  /** `HH:mm`; `null` — часы или минуты вне диапазона. */
  time: string | null
}

function normalizeLine(raw: string): string {
  return raw
    .replace(/\u00a0/g, ' ')
    .replace(/[|;\t]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

function padTwo(value: number): string {
  return String(value).padStart(2, '0')
}

function isValidDate(year: number, month: number, day: number): boolean {
  const probe = new Date(Date.UTC(year, month - 1, day))

  return (
    probe.getUTCFullYear() === year
    && probe.getUTCMonth() === month - 1
    && probe.getUTCDate() === day
  )
}

/** Ближайшее вхождение дня недели, начиная с сегодня. */
function weekdayDate(today: string, weekday: number): string {
  const start = Date.parse(`${today}T00:00:00Z`)

  if (Number.isNaN(start)) {
    return today
  }

  const todayWeekday = new Date(start).getUTCDay()

  return addDays(today, (weekday - todayWeekday + 7) % 7)
}

function monthFromWord(word: string): number | null {
  return MONTH_WORDS.get(word.toLowerCase()) ?? null
}

function weekdayFromWord(word: string): number | null {
  const lower = word.toLowerCase()

  for (const [stem, weekday] of WEEKDAY_STEMS) {
    if (lower === stem) {
      return weekday
    }
  }

  return null
}

/** Год без года — текущий; прошедшая больше чем на сутки дата — следующий. */
function resolveYear(today: string, year: number | null, month: number, day: number): string | null {
  if (!isValidDate(year ?? 2000, month, day)) {
    return null
  }

  if (year !== null) {
    return `${year}-${padTwo(month)}-${padTwo(day)}`
  }

  const date = (candidate: number): string =>
    `${candidate}-${padTwo(month)}-${padTwo(day)}`

  const currentYear = Number(today.slice(0, 4))
  const current = date(currentYear)

  return current < addDays(today, -1) ? date(currentYear + 1) : current
}

function findNumericDate(text: string, today: string): DateHit | null {
  const pattern = /(?<!\d)(\d{1,2})[./](\d{1,2})(?:[./](\d{2}|\d{4}))?(?!\d)/
  const match = pattern.exec(text)

  if (match === null) {
    return null
  }

  const day = Number(match[1])
  const month = Number(match[2])
  const rawYear = match[3]
  const year =
    rawYear === undefined
      ? null
      : rawYear.length === 2
        ? 2000 + Number(rawYear)
        : Number(rawYear)

  return {
    start: match.index,
    end: match.index + match[0].length,
    date: resolveYear(today, year, month, day),
  }
}

function findMonthNameDate(text: string, today: string): DateHit | null {
  const pattern = /(?<!\d)(\d{1,2})\s+([\p{L}]+)/gu

  for (const match of text.matchAll(pattern)) {
    const month = monthFromWord(match[2])

    if (month === null) {
      continue
    }

    const day = Number(match[1])

    // «00 Мария» — не дата: число до месяца должно быть числом месяца.
    if (day < 1 || day > 31) {
      continue
    }

    return {
      start: match.index,
      end: match.index + match[0].length,
      date: resolveYear(today, null, month, day),
    }
  }

  return null
}

function findRelativeDate(text: string, today: string): DateHit | null {
  for (const [word, shift] of RELATIVE_DAYS) {
    const pattern = new RegExp(`(^|[^\\p{L}])${word}(?=[^\\p{L}]|$)`, 'iu')
    const match = pattern.exec(text)

    if (match === null) {
      continue
    }

    const start = match.index + match[1].length

    return { start, end: start + word.length, date: addDays(today, shift) }
  }

  return null
}

function findDate(text: string, today: string): DateHit | null {
  return (
    findNumericDate(text, today)
    ?? findMonthNameDate(text, today)
    ?? findRelativeDate(text, today)
  )
}

function findWeekday(text: string): { start: number; end: number; weekday: number } | null {
  const pattern = /(^|[^\p{L}])([\p{L}]+)(?=[^\p{L}]|$)/gu

  for (const match of text.matchAll(pattern)) {
    const weekday = weekdayFromWord(match[2])

    if (weekday === null) {
      continue
    }

    const start = match.index + match[1].length

    return { start, end: start + match[2].length, weekday }
  }

  return null
}

function findTime(text: string): TimeHit | null {
  const pattern = /(?<!\d)(\d{1,2})[:-](\d{2})(?!\d)/
  const match = pattern.exec(text)

  if (match === null) {
    return null
  }

  const hours = Number(match[1])
  const minutes = Number(match[2])

  return {
    start: match.index,
    end: match.index + match[0].length,
    time: hours > 23 || minutes > 59 ? null : `${padTwo(hours)}:${padTwo(minutes)}`,
  }
}

function emptyDraft(line: number, raw: string): ImportDraft {
  return {
    line,
    raw,
    date: null,
    time: null,
    startAt: null,
    endAt: null,
    firstName: null,
    lastName: null,
    phone: null,
    salonId: null,
    serviceId: null,
    serviceCandidates: [],
    issues: [],
    source: 'text',
    externalUid: null,
    recurrenceId: null,
    eventSummary: null,
    eventStartAt: null,
    eventEndAt: null,
    eventDurationMinutes: null,
    useEventTime: true,
    sourceTimeZone: null,
    hints: [],
    defaultIncluded: true,
  }
}

type LineResult =
  | { kind: 'skip' }
  | { kind: 'header'; date: string }
  | { kind: 'row'; draft: ImportDraft }

function parseLine(
  raw: string,
  line: number,
  options: ImportParseOptions,
  contextDate: string | null
): LineResult {
  let text = normalizeLine(raw)

  if (text === '') {
    return { kind: 'skip' }
  }

  const headerTokens = collapse(flatten(text)).split(' ')

  if (
    headerTokens.length >= 2
    && headerTokens.every((token) => HEADER_WORDS.has(token))
  ) {
    return { kind: 'skip' }
  }

  const draft = emptyDraft(line, raw)
  const issues = draft.issues

  const phone = findPhone(text)

  if (phone !== null) {
    draft.phone = phone.issue === null ? phone.phone : phone.raw

    if (phone.issue !== null) {
      issues.push(phone.issue)
    }

    text = removeRange(text, phone.start, phone.end)
  }

  const date = findDate(text, options.today)

  if (date !== null) {
    text = removeRange(text, date.start, date.end)

    if (date.date === null) {
      issues.push('badDate')
    } else {
      draft.date = date.date
    }
  }

  const weekday = findWeekday(text)

  if (weekday !== null) {
    text = removeRange(text, weekday.start, weekday.end)
  }

  // Без явной даты день недели и есть дата — ближайшее вхождение.
  if (draft.date === null && weekday !== null && date === null) {
    draft.date = weekdayDate(options.today, weekday.weekday)
  }

  const time = findTime(text)

  if (time !== null) {
    text = removeRange(text, time.start, time.end)

    if (time.time === null) {
      issues.push('badTime')
    } else {
      draft.time = time.time
    }
  }

  // Заголовок даты: после удаления времени и телефона не осталось ничего, кроме
  // даты. Своих записей такая строка не создаёт и задаёт дату следующим.
  if (text.replace(/[^\p{L}\p{N}]+/gu, '') === '') {
    if (draft.date !== null && issues.length === 0) {
      return { kind: 'header', date: draft.date }
    }

    if (draft.date !== null) {
      // Битая дата в шапке не должна молча съесть строку: это запись без имени.
      draft.salonId = options.defaultSalonId === '' ? null : options.defaultSalonId
      issues.push('noName', 'noService')

      return { kind: 'row', draft }
    }

    issues.push('noDate', 'noTime', 'noName', 'noService')

    if (draft.phone === null) {
      issues.push('noPhone')
    }

    return { kind: 'row', draft }
  }

  const service = findService(text, options.services)

  if (service.match !== null) {
    draft.serviceId = service.match.service.barberServiceId
    text = removeRange(
      text,
      service.match.start,
      service.match.start + service.match.length
    )
  } else if (service.ambiguous.length > 0) {
    draft.serviceCandidates = service.ambiguous.map((item) => item.barberServiceId)
    issues.push('ambiguousService')
  } else {
    issues.push('noService')
  }

  const salon = findSalon(text, options.salons)

  if (salon !== null) {
    draft.salonId = salon.salon.salonId
    text = removeRange(text, salon.start, salon.start + salon.length)
  } else {
    draft.salonId = options.defaultSalonId === '' ? null : options.defaultSalonId
  }

  const name = splitName(text)

  if (name === null) {
    issues.push('noName')
  } else {
    draft.firstName = name.firstName
    draft.lastName = name.lastName
  }

  if (draft.date === null) {
    if (issues.includes('badDate')) {
      // «29 февраля» уже объяснена: второй ошибки про дату не добавляем.
    } else if (contextDate === null) {
      issues.push('noDate')
    } else {
      // Дата из шапки выше: строка записи её наследует.
      draft.date = contextDate
    }
  }

  if (draft.time === null && !issues.includes('badTime')) {
    issues.push('noTime')
  }

  if (draft.phone === null) {
    issues.push('noPhone')
  }

  draft.issues = sortIssues(issues)
  applyTiming(draft, options)
  return { kind: 'row', draft }
}

/**
 * Порядок ошибок задаёт текст в строке предпросмотра: причина важнее следствия.
 *
 * `overlap` и `tooFar` — серверные ограничения, поэтому стоят последними: порядок
 * прежних кодов и тексты текстового импорта от расширения не меняются.
 */
const ISSUE_ORDER: readonly ImportIssue[] = [
  'badDate',
  'noDate',
  'badTime',
  'noTime',
  'badPhone',
  'noPhone',
  'noName',
  'ambiguousService',
  'noService',
  'past',
  'duplicate',
  'overlap',
  'tooFar',
]

function sortIssues(issues: readonly ImportIssue[]): ImportIssue[] {
  const unique = [...new Set(issues)]

  return unique.sort(
    (left, right) => ISSUE_ORDER.indexOf(left) - ISSUE_ORDER.indexOf(right)
  )
}

/** Итоговая длительность допустима: конец позже начала и не длиннее 12 часов. */
function isValidImportDuration(minutes: number): boolean {
  return minutes > 0 && minutes <= MAX_IMPORT_DURATION_MINUTES
}

function pushIssue(draft: ImportDraft, issue: ImportIssue): void {
  if (!draft.issues.includes(issue)) {
    draft.issues.push(issue)
  }
}

function addHint(draft: ImportDraft, hint: ImportHint): void {
  if (!draft.hints.includes(hint)) {
    draft.hints.push(hint)
  }
}

function endFromDuration(startAt: string, durationMinutes: number): string {
  return new Date(
    Date.parse(startAt) + durationMinutes * 60_000
  ).toISOString()
}

/** Разница между началом и концом в допустимых пределах. */
function hasValidSpan(startAt: string, endAt: string): boolean {
  const startMs = Date.parse(startAt)
  const endMs = Date.parse(endAt)

  if (Number.isNaN(startMs) || Number.isNaN(endMs)) {
    return false
  }

  return isValidImportDuration((endMs - startMs) / 60_000)
}

/**
 * Последний шанс для конца — длительность услуги.
 *
 * Сюда попадают, когда ни конец события, ни его длительность не дали интервала.
 * Нулевая или слишком длинная услуга — `noService`, как у текста: создать такую
 * запись нельзя, и показывать время нечем.
 */
function serviceEndAt(
  draft: ImportDraft,
  startAt: string,
  serviceMinutes: number
): string | null {
  if (!isValidImportDuration(serviceMinutes)) {
    pushIssue(draft, 'noService')

    return null
  }

  addHint(draft, 'durationMismatch')

  return endFromDuration(startAt, serviceMinutes)
}

/** Окно сервера: раньше `now + 5 минут` — `past`, дальше 90 дней — `tooFar`. */
function markIcsWindow(
  draft: ImportDraft,
  startAt: string,
  options: ImportParseOptions
): void {
  const startMs = Date.parse(startAt)

  if (startMs < options.now + PAST_TOLERANCE_MS) {
    pushIssue(draft, 'past')
  }

  if (startMs > options.now + FAR_TOLERANCE_MS) {
    pushIssue(draft, 'tooFar')
  }
}

/**
 * Время строки календаря, пока она представляет момент события.
 *
 * Абсолютный инстант — источник истины: правка салона меняет только отображение,
 * поэтому дата и время пересчитываются в поясе выбранного салона. Конец берётся
 * из `DTEND`, если тот даёт допустимый интервал, иначе из длительности события.
 */
function applyIcsEventTiming(
  draft: ImportDraft,
  startAt: string,
  serviceMinutes: number,
  timeZoneId: string,
  options: ImportParseOptions
): void {
  let endAt: string | null = null

  if (draft.eventEndAt !== null && hasValidSpan(startAt, draft.eventEndAt)) {
    endAt = draft.eventEndAt
  } else {
    const durationMinutes = draft.eventDurationMinutes ?? serviceMinutes
    const candidate = endFromDuration(startAt, durationMinutes)

    endAt = hasValidSpan(startAt, candidate)
      ? candidate
      : serviceEndAt(draft, startAt, serviceMinutes)
  }

  if (endAt === null) {
    return
  }

  draft.startAt = startAt
  draft.endAt = endAt
  // Показываем момент в поясе выбранного салона: 10:00Z — это 15:00 в
  // Екатеринбурге и 11:00 в Берлине, поэтому смена салона меняет дату и время.
  draft.date = salonDateFromInstant(startAt, timeZoneId)
  draft.time = salonTimeFromInstant(startAt, timeZoneId)
  markIcsWindow(draft, startAt, options)
}

/**
 * Время строки календаря после правки даты/времени или для floating-события.
 *
 * Настенное время пользователя важнее момента: оно пересчитывается в UTC поясом
 * салона и обратно не переписывается. Длительность события сохраняется, даже
 * если время сдвинули.
 */
function applyIcsWallClockTiming(
  draft: ImportDraft,
  timeZoneId: string,
  serviceMinutes: number,
  options: ImportParseOptions
): void {
  if (draft.date === null || draft.time === null) {
    return
  }

  const startAt = salonDateTimeToUtc(draft.date, draft.time, timeZoneId)

  if (startAt === null) {
    // Весной локального часа не существует: `salonDateTimeToUtc` вернул `null`.
    pushIssue(draft, 'badTime')

    return
  }

  const durationMinutes = draft.eventDurationMinutes ?? serviceMinutes
  const candidate = endFromDuration(startAt, durationMinutes)
  const endAt = hasValidSpan(startAt, candidate)
    ? candidate
    : serviceEndAt(draft, startAt, serviceMinutes)

  if (endAt === null) {
    return
  }

  draft.startAt = startAt
  draft.endAt = endAt
  markIcsWindow(draft, startAt, options)
}

/** Время строки календаря: момент события, а после правки времени — настенные часы. */
function applyIcsTiming(draft: ImportDraft, options: ImportParseOptions): void {
  if (draft.serviceId === null || draft.salonId === null) {
    return
  }

  const service = options.services.find(
    (item) => item.barberServiceId === draft.serviceId
  )
  const salon = options.salons.find((item) => item.salonId === draft.salonId)

  if (service === undefined || salon === undefined) {
    return
  }

  if (draft.useEventTime && draft.eventStartAt !== null) {
    if (Number.isNaN(Date.parse(draft.eventStartAt))) {
      pushIssue(draft, 'badTime')

      return
    }

    applyIcsEventTiming(
      draft,
      draft.eventStartAt,
      service.durationMinutes,
      salon.timeZoneId,
      options
    )

    return
  }

  applyIcsWallClockTiming(
    draft,
    salon.timeZoneId,
    service.durationMinutes,
    options
  )
}

/**
 * `startAt`/`endAt` и проверка прошлого для вставленного списка.
 *
 * Время переводится в UTC поясом салона: без этого «10:00» в Екатеринбурге стало
 * бы «10:00Z» и на сервер ушла бы другая запись. Нулевая длительность услуги —
 * тоже `noService`: конец визита совпал бы с началом.
 */
function applyTextTiming(draft: ImportDraft, options: ImportParseOptions): void {
  if (draft.date === null || draft.time === null) {
    return
  }

  if (draft.serviceId === null || draft.salonId === null) {
    return
  }

  const service = options.services.find(
    (item) => item.barberServiceId === draft.serviceId
  )
  const salon = options.salons.find((item) => item.salonId === draft.salonId)

  if (service === undefined || salon === undefined) {
    return
  }

  if (service.durationMinutes <= 0) {
    if (!draft.issues.includes('noService')) {
      draft.issues.push('noService')
    }

    return
  }

  const startAt = salonDateTimeToUtc(draft.date, draft.time, salon.timeZoneId)

  if (startAt === null) {
    // Весной локального часа не существует: `salonDateTimeToUtc` вернул `null`.
    if (!draft.issues.includes('badTime')) {
      draft.issues.push('badTime')
    }

    return
  }

  draft.startAt = startAt
  draft.endAt = new Date(
    Date.parse(startAt) + service.durationMinutes * 60_000
  ).toISOString()

  if (Date.parse(startAt) < options.now + PAST_TOLERANCE_MS) {
    draft.issues.push('past')
  }
}

/** Диспетчер: текст считает время настенными часами, календарь — моментом события. */
function applyTiming(draft: ImportDraft, options: ImportParseOptions): void {
  if (draft.source === 'ics') {
    applyIcsTiming(draft, options)

    return
  }

  applyTextTiming(draft, options)
}

/** Дубль текста: та же пара «салон — время» или «телефон — время». */
function markTextDuplicates(rows: readonly ImportDraft[]): void {
  const seen = new Set<string>()

  for (const row of rows) {
    if (
      row.source === 'ics'
      || row.startAt === null
      || row.issues.length > 0
    ) {
      continue
    }

    const slots = [`${row.salonId}|${row.startAt}`]

    if (row.phone !== null) {
      slots.push(`${row.phone}|${row.startAt}`)
    }

    if (slots.some((key) => seen.has(key))) {
      pushIssue(row, 'duplicate')
      continue
    }

    for (const key of slots) {
      seen.add(key)
    }
  }
}

/**
 * Ключи дубля события: идентичность `UID` + `RECURRENCE-ID`, затем время с тем же
 * телефоном, а без телефона — с тем же именем.
 */
function icsDuplicateKeys(row: ImportDraft): string[] {
  const keys: string[] = []

  if (row.externalUid !== null) {
    keys.push(`${row.externalUid}\0${row.recurrenceId ?? ''}`)
  }

  if (row.startAt === null) {
    return keys
  }

  const phone = row.phone === null ? null : normalizeImportPhone(row.phone)

  if (phone !== null) {
    keys.push(`phone\0${row.startAt}\0${phone}`)
  } else if (row.firstName !== null) {
    keys.push(`name\0${row.startAt}\0${collapse(flatten(row.firstName))}`)
  }

  return keys
}

/**
 * Дубль события календаря: та же идентичность события или то же время с тем же
 * телефоном (без телефона — с тем же именем). Вторая по номеру строка получает
 * `duplicate`.
 */
function markIcsDuplicates(rows: readonly ImportDraft[]): void {
  const seen = new Set<string>()
  const candidates = rows
    .filter((row) => row.source === 'ics' && row.issues.length === 0)
    .sort((left, right) => left.line - right.line)

  for (const row of candidates) {
    const keys = icsDuplicateKeys(row)

    if (keys.some((key) => seen.has(key))) {
      pushIssue(row, 'duplicate')
      continue
    }

    for (const key of keys) {
      seen.add(key)
    }
  }
}

function markDuplicates(rows: readonly ImportDraft[]): void {
  markTextDuplicates(rows)
  markIcsDuplicates(rows)
}

/** Интервал строки календаря; у текста и строк без обоих моментов его нет. */
function icsInterval(row: ImportDraft): { start: number; end: number } | null {
  if (row.source !== 'ics' || row.startAt === null || row.endAt === null) {
    return null
  }

  const start = Date.parse(row.startAt)
  const end = Date.parse(row.endAt)

  if (Number.isNaN(start) || Number.isNaN(end)) {
    return null
  }

  return { start, end }
}

/** Полуинтервалы `[start, end)` пересекаются: конец одного позже начала другого. */
function intervalsIntersect(
  left: { start: number; end: number },
  right: { start: number; end: number }
): boolean {
  return left.start < right.end && right.start < left.end
}

/** Порядок строк календаря: по началу, при равенстве — по номеру строки. */
function compareIcsRows(left: ImportDraft, right: ImportDraft): number {
  const leftStart = left.startAt === null ? 0 : Date.parse(left.startAt)
  const rightStart = right.startAt === null ? 0 : Date.parse(right.startAt)

  if (leftStart !== rightStart) {
    return leftStart - rightStart
  }

  return left.line - right.line
}

/**
 * Пересечения строк календаря: вторая по времени строка получает `overlap`.
 *
 * Салон не важен — барбер не может быть в двух местах. Строка с уже найденным
 * `overlap` остаётся источником конфликта, поэтому третья строка, пересекающая
 * только вторую, тоже помечается.
 */
function markOverlaps(
  rows: readonly ImportDraft[],
  included?: ReadonlySet<number>
): void {
  const candidates = rows
    .filter((row) => included === undefined || included.has(row.line))
    .filter((row) => icsInterval(row) !== null)
    .sort(compareIcsRows)
  const earlier: ImportDraft[] = []

  for (const row of candidates) {
    const interval = icsInterval(row)

    if (interval === null) {
      continue
    }

    const overlaps = earlier.some((other) => {
      const otherInterval = icsInterval(other)

      return otherInterval !== null && intervalsIntersect(otherInterval, interval)
    })

    if (overlaps) {
      pushIssue(row, 'overlap')
    }

    earlier.push(row)
  }
}

/**
 * Партнёр пересечения для подписи «Пересекается с записью «…»».
 *
 * Возвращается самая ранняя строка календаря, которая начинается раньше данной
 * (при равном начале — с меньшим номером) и пересекает её интервал. `included`
 * ограничивает поиск выбранными строками: снятая галочка снимает конфликт.
 */
export function findOverlapPartner(
  rows: readonly ImportDraft[],
  line: number,
  included?: ReadonlySet<number>
): ImportDraft | null {
  const target = rows.find((row) => row.line === line)
  const targetInterval = target === undefined ? null : icsInterval(target)

  if (target === undefined || targetInterval === null) {
    return null
  }

  let partner: ImportDraft | null = null

  for (const candidate of rows) {
    const interval = icsInterval(candidate)

    if (interval === null || candidate.line === line) {
      continue
    }

    if (included !== undefined && !included.has(candidate.line)) {
      continue
    }

    if (compareIcsRows(candidate, target) >= 0) {
      continue
    }

    if (!intervalsIntersect(interval, targetInterval)) {
      continue
    }

    if (partner === null || compareIcsRows(candidate, partner) < 0) {
      partner = candidate
    }
  }

  return partner
}

export type ImportRowPatch = {
  date?: string | null
  time?: string | null
  firstName?: string | null
  lastName?: string | null
  phone?: string | null
  salonId?: string | null
  serviceId?: string | null
  /** Строка больше не показывает момент события: время считается из даты/времени. */
  useEventTime?: boolean
  /** Длительность события; подстановка слота сбрасывает её в `null`. */
  eventDurationMinutes?: number | null
}

function deriveIssues(row: ImportDraft): ImportIssue[] {
  const issues: ImportIssue[] = []

  if (row.date === null) {
    issues.push('noDate')
  }

  if (row.time === null) {
    issues.push('noTime')
  }

  if (row.phone === null) {
    issues.push('noPhone')
  } else if (!isImportPhone(row.phone)) {
    issues.push('badPhone')
  }

  if (row.firstName === null) {
    issues.push('noName')
  }

  if (row.serviceId === null) {
    issues.push(row.serviceCandidates.length > 0 ? 'ambiguousService' : 'noService')
  }

  return issues
}

/** Пересчёт одной строки: ошибки полей выводятся из значений, время — из пояса салона. */
function recomputeRow(row: ImportDraft, options: ImportParseOptions): ImportDraft {
  const next: ImportDraft = {
    ...row,
    startAt: null,
    endAt: null,
    serviceCandidates: row.serviceId === null ? row.serviceCandidates : [],
    issues: deriveIssues(row),
    // Подсказки календаря переживают пересчёт: `durationMismatch` добавляется к ним,
    // а не заменяет их.
    hints: [...row.hints],
  }

  applyTiming(next, options)

  return next
}

/** Правка строки из предпросмотра: всё производное — время, прошлое, готовность — заново. */
export function applyImportRowPatch(
  row: ImportDraft,
  patch: ImportRowPatch
): ImportDraft {
  // Правка даты или времени означает, что строка больше не показывает момент
  // события: время считается из настенных часов, а не из исходного инстанта.
  const editsWallClock = patch.date !== undefined || patch.time !== undefined

  return {
    ...row,
    ...patch,
    ...(editsWallClock ? { useEventTime: false } : {}),
    // Правка услуги снимает неоднозначность: кандидаты больше не нужны.
    ...(patch.serviceId === undefined ? {} : { serviceCandidates: [] }),
  }
}

/**
 * Пересчёт пачки после правки.
 *
 * Ошибки полей выводятся из значений, поэтому исправленная ячейка сразу гасит
 * свою ошибку, а дубли и пересечения считаются заново по всей пачке: первая
 * строка пары снова становится готовой. `included` ограничивает пересечения
 * выбранными строками; без него участвуют все.
 */
export function recomputeImportRows(
  rows: readonly ImportDraft[],
  options: ImportParseOptions,
  included?: ReadonlySet<number>
): ImportDraft[] {
  const next = rows.map((row) => recomputeRow(row, options))

  markDuplicates(next)
  markOverlaps(next, included)

  for (const row of next) {
    row.issues = sortIssues(row.issues)
  }

  return next
}

/**
 * Текст → черновики записей.
 *
 * Больше 50 непустых строк — ошибка формы, разбор не запускается: пачка из сотни
 * последовательных `POST` не должна начинаться, если её нельзя довести до конца.
 */
export function parseImportText(
  text: string,
  options: ImportParseOptions
): ImportParseResult {
  const lines = text.split(LINE_BREAK)
  const filled = lines.filter((line) => line.trim() !== '')

  if (filled.length === 0) {
    return { rows: [], failure: 'empty' }
  }

  if (filled.length > IMPORT_MAX_ROWS) {
    return { rows: [], failure: 'tooManyRows' }
  }

  const rows: ImportDraft[] = []
  let contextDate: string | null = null

  for (let index = 0; index < lines.length; index += 1) {
    const raw = lines[index]

    if (raw.trim() === '') {
      continue
    }

    const result = parseLine(raw, index + 1, options, contextDate)

    if (result.kind === 'header') {
      contextDate = result.date
      continue
    }

    if (result.kind === 'row') {
      rows.push(result.draft)
    }
  }

  for (const row of rows) {
    row.issues = sortIssues(row.issues)
  }

  markDuplicates(rows)

  return { rows, failure: null }
}

// Публичный API модуля не менялся: правка телефона в предпросмотре и проверка
// готовности по-прежнему берутся отсюда, хотя живут в `import-match.ts`.
export { isImportPhone, normalizeImportPhone }
