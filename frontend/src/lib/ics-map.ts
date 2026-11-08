/**
 * Сопоставление вхождений календаря с черновиками импорта.
 *
 * Вход — уже разобранные `IcsOccurrence` (абсолютные моменты, `UID`, тексты), выход —
 * `ImportDraft`, который показывает предпросмотр и отправляет хук. Функция чистая:
 * ни сети, ни React, ни состояния, поэтому один и тот же файл даёт один и тот же
 * предпросмотр, а порядок строк повторяет порядок вхождений.
 *
 * ## Почему телефон ищется первым
 *
 * Телефон — единственное поле, которое нельзя восстановить после того, как цифры
 * попали в имя или в дату: «+7 999 123-45-67» в остатке строки выглядит как набор
 * чисел, а «12.11» уже похоже на время. Поэтому номер вырезается до услуги и
 * имени, и только остаток уходит в `splitName`.
 *
 * ## Почему момент события — источник истины
 *
 * `TZID`/`Z` в файле задают момент; `date`/`time` — только его проекция в пояс
 * выбранного салона. Правка салона не должна переинтерпретировать 10:00 как
 * 10:00 другого пояса, поэтому строка хранит `eventStartAt`/`eventEndAt`, а
 * отображение пересчитывается из момента.
 *
 * ## Почему floating-события не сохраняют момент
 *
 * У события без `TZID` абсолютного момента не существует: 10:00 — это настенное
 * время, и при смене салона оно должно остаться 10:00, а не уехать на разницу
 * поясов. Таким строкам `useEventTime = false`: время считается от пояса
 * выбранного салона, как у правки даты/времени.
 *
 * ## Почему HTML вырезается
 *
 * `.ics` — недоверенный ввод: Outlook и Google кладут в `DESCRIPTION` настоящий
 * HTML. Мы никогда не рендерим его, а для эвристик теги только мешают: телефон и
 * услуга должны искаться по тексту, а не по разметке.
 */

import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import {
  ICS_MAX_DURATION_MINUTES,
  type IcsOccurrence,
  type IcsParseOptions,
} from '@/lib/ics-parse'
import type { ImportDraft, ImportHint, ImportIssue } from '@/lib/import-parse'
import {
  collapse,
  findPhone,
  findService,
  flatten,
  matchName,
  pickBest,
  removeRange,
  splitName,
  type MatchTier,
  type PhoneHit,
} from '@/lib/import-match'
import { salonDateFromInstant, salonTimeFromInstant } from '@/lib/salon-time'

/** Предел текста `SUMMARY`/`LOCATION` для подписи строки и эвристик. */
const TEXT_LIMIT = 4000

/** Сырой `DESCRIPTION` режется до разбора: файл — недоверенный ввод. */
const DESCRIPTION_LIMIT = 4000

/** Лимиты гостя из API: имя и фамилия ≤ 100, телефон ≤ 32. */
const NAME_LIMIT = 100
const PHONE_LIMIT = 32

/**
 * Порог расхождения длительности события и услуги.
 *
 * Сравнение включительное: 45 минут события при 60 минутах услуги — это уже
 * заметная разница (её и показывает текст подсказки), а не «почти совпало».
 */
const DURATION_MISMATCH_MINUTES = 15

// ---------------------------------------------------------------------------
// Текст: HTML → plain text, затем шум встреч.
// ---------------------------------------------------------------------------

const TAG_PATTERN = /<[^>]*>/g
const ENTITY_PATTERN = /&(#x[0-9a-f]+|#\d+|[a-zA-Z]+);/gi

const NAMED_ENTITIES: Readonly<Record<string, string>> = {
  amp: '&',
  lt: '<',
  gt: '>',
  quot: '"',
  apos: "'",
  nbsp: ' ',
}

const URL_PATTERN = /\b(?:https?:\/\/|www\.)\S+/gi

/** Слова-маркеры встречи: в тексте для эвристик они не значат ничего. */
const MEETING_NOISE =
  /google\s*meet|zoom|телемост|dial-?in|join\s+with[….\s]*/gi

const MEETING_LOCATION =
  /https?:\/\/|www\.|google\s*meet|zoom|телемост|dial-?in|join\s+with/i

const PHONE_LABEL = /тел|телефон|phone/i

function decodeEntity(entity: string): string | null {
  const lower = entity.toLowerCase()

  if (lower.startsWith('#x')) {
    const code = Number.parseInt(lower.slice(2), 16)
    return isCharCode(code) ? String.fromCodePoint(code) : null
  }

  if (lower.startsWith('#')) {
    const code = Number.parseInt(lower.slice(1), 10)
    return isCharCode(code) ? String.fromCodePoint(code) : null
  }

  return NAMED_ENTITIES[lower] ?? null
}

function isCharCode(code: number): boolean {
  return Number.isFinite(code) && code >= 0 && code <= 0x10ffff
}

function stripTags(value: string): string {
  return value.replace(TAG_PATTERN, ' ')
}

function decodeEntities(value: string): string {
  return value.replace(
    ENTITY_PATTERN,
    (whole: string, entity: string) => decodeEntity(entity) ?? whole
  )
}

/**
 * HTML → текст.
 *
 * Два прохода удаления тегов: некоторые экспорты кладут в `DESCRIPTION` уже
 * экранированный HTML (`&lt;p&gt;…`), и теги появляются только после раскрытия
 * сущностей. Текст остаётся текстом — он нигде не рендерится как разметка.
 */
function toPlainText(value: string): string {
  return stripTags(decodeEntities(stripTags(value)))
}

/**
 * Убирает ссылки и слова встреч, сохраняя переводы строк.
 *
 * Переводы строк важны телефону: подпись «Тел.:» ищется построчно, а номер из
 * ссылки на Google Meet не должен становиться телефоном клиента. Горизонтальные
 * пробелы схлопываются, чтобы найденные индексы оставались предсказуемыми.
 */
function removeNoise(value: string): string {
  return value
    .replace(URL_PATTERN, ' ')
    .replace(MEETING_NOISE, ' ')
    .replace(/[^\S\n]+/g, ' ')
    .replace(/\n{2,}/g, '\n')
    .trim()
}

function isMeetingLocation(value: string): boolean {
  return MEETING_LOCATION.test(value)
}

// ---------------------------------------------------------------------------
// Телефон.
// ---------------------------------------------------------------------------

type PhoneOrigin = 'description' | 'location' | 'summary'

type PhoneSource = {
  origin: PhoneOrigin
  text: string
}

/**
 * Телефон в тексте: сначала строка с подписью, иначе первый найденный номер.
 *
 * `findPhone` ищет по строке и возвращает индексы относительно неё, поэтому
 * смещение строки прибавляется к найденному диапазону — иначе вырезание номера
 * из `SUMMARY` съело бы не тот фрагмент.
 */
function findPhonePreferred(text: string): PhoneHit | null {
  let offset = 0

  for (const line of text.split('\n')) {
    if (PHONE_LABEL.test(line)) {
      const hit = findPhone(line)

      if (hit !== null) {
        return { ...hit, start: hit.start + offset, end: hit.end + offset }
      }
    }

    offset += line.length + 1
  }

  return findPhone(text)
}

function resolvePhone(
  sources: readonly PhoneSource[]
): { origin: PhoneOrigin | null; hit: PhoneHit | null } {
  for (const source of sources) {
    if (source.text === '') {
      continue
    }

    const hit = findPhonePreferred(source.text)

    if (hit !== null) {
      return { origin: source.origin, hit }
    }
  }

  return { origin: null, hit: null }
}

// ---------------------------------------------------------------------------
// Салон.
// ---------------------------------------------------------------------------

type SalonMatch = {
  salon: BarberProfileSalon
  start: number
  length: number
}

/**
 * Салон по названию, городу или адресу.
 *
 * `findSalon` знает только название и город, поэтому адрес («Ленина 1») в
 * `LOCATION` не находился бы. Адрес добавляется тем же `matchName`, а лучший
 * кандидат выбирается общим `pickBest`: точное совпадение важнее совпадения
 * внутри строки, длинное — короткого.
 */
function findSalonWithAddress(
  text: string,
  salons: readonly BarberProfileSalon[]
): SalonMatch | null {
  const candidates: {
    value: BarberProfileSalon
    tier: MatchTier
    start: number
    length: number
  }[] = []

  for (const salon of salons) {
    let best: { tier: MatchTier; start: number; length: number } | null = null

    for (const value of [salon.name, salon.city, salon.address]) {
      if (value.trim() === '') {
        continue
      }

      const hit = matchName(text, value)

      if (hit === null) {
        continue
      }

      if (
        best === null
        || hit.tier < best.tier
        || (hit.tier === best.tier && hit.length > best.length)
      ) {
        best = hit
      }
    }

    if (best !== null) {
      candidates.push({ value: salon, ...best })
    }
  }

  const best = pickBest(candidates)

  return best === null
    ? null
    : { salon: best.value, start: best.start, length: best.length }
}

// ---------------------------------------------------------------------------
// Имя.
// ---------------------------------------------------------------------------

const NAME_URL = /\b(?:https?:\/\/|www\.)\S+/gi
const NAME_DATE_DOTTED = /(?<!\d)\d{1,2}\.\d{1,2}(?:\.\d{2,4})?(?!\d)/g
const NAME_DATE_MONTH =
  /(?<!\d)\d{1,2}\s+(?:янв|фев|мар|апр|июн|июл|авг|сен|окт|ноя|дек|ма)\p{L}*(?:\s+\d{4})?/giu
const NAME_TIME = /(?<!\d)\d{1,2}[:.]\d{2}(?!\d)/g
const NAME_RELATIVE =
  /(?<!\p{L})(?:послезавтра|завтра|сегодня)(?!\p{L})/giu
const NAME_STOP_WORDS =
  /(?<!\p{L})(?:appointment|booking|клиентка|клиент|запись|телефон|тел|phone|в|на|к|для)(?!\p{L})/giu

/**
 * Остаток `SUMMARY` перед `splitName`: время, даты, относительные слова, ссылки и
 * служебные слова имени не принадлежат.
 *
 * Телефон, услуга и салон уже вырезаны из того же остатка, поэтому здесь
 * остаются только «шумовые» токены. Даты убираются до времени: «12.11» похоже на
 * «12:11», и обратный порядок оставил бы от даты хвост.
 */
function cleanNameText(value: string): string {
  return value
    .replace(NAME_URL, ' ')
    .replace(NAME_DATE_DOTTED, ' ')
    .replace(NAME_DATE_MONTH, ' ')
    .replace(NAME_TIME, ' ')
    .replace(NAME_RELATIVE, ' ')
    .replace(NAME_STOP_WORDS, ' ')
}

/** Явные метки в описании: «Клиент:», «Имя:», «Client:», «Name:». */
const NAME_LABEL =
  /(?<!\p{L})(?:клиент|имя|client|name)\s*[:\-–—]\s*([^\n]+)/iu

/**
 * Остаток строки с подписью: всё, что после метки, чистится так же, как `SUMMARY`.
 *
 * Телефон вырезается первым и по индексам `findPhone`, а не регуляркой: в
 * «Клиент: Иван Петров, тел. +7…» иначе «тел» осталось бы фамилией. Остальные
 * шумовые токены (ссылки, даты, время, служебные слова) убирает `cleanNameText`.
 */
function cleanLabeledName(value: string): string {
  let cleaned = value
  let phone = findPhone(cleaned)

  while (phone !== null) {
    cleaned = removeRange(cleaned, phone.start, phone.end)
    phone = findPhone(cleaned)
  }

  return collapse(cleanNameText(cleaned))
}

function findLabeledName(description: string): string | null {
  const match = NAME_LABEL.exec(description)

  return match === null ? null : cleanLabeledName(match[1])
}

// ---------------------------------------------------------------------------
// Personal-эвристика.
// ---------------------------------------------------------------------------

/**
 * Маркеры личного события. `flatten` сводит `ё` к `е`, поэтому основы записаны
 * без `ё`; проверяются только как подстроки — это мягкая подсказка, не ошибка.
 */
const PERSONAL_MARKERS: readonly string[] = [
  'обед',
  'перерыв',
  'выходн',
  'отпуск',
  'больничн',
  'личн',
  'занят',
  'busy',
  'block',
  'встреч',
  'созвон',
  'zoom',
  'google meet',
  'доставк',
  'тренировк',
  'врач',
  'стоматолог',
  'учеб',
  'дорога',
  'пробк',
]

// ---------------------------------------------------------------------------
// Порядок подсказок и ошибок.
// ---------------------------------------------------------------------------

/** Порядок задаёт текст в строке предпросмотра; набор — только блокирующие коды. */
const HINT_ORDER: readonly ImportHint[] = [
  'personal',
  'free',
  'tentative',
  'noContact',
  'salonFallback',
  'durationMismatch',
  'timeZoneConverted',
  'phoneFromDescription',
  'nameTruncated',
  'phoneTruncated',
]

const ISSUE_ORDER: readonly ImportIssue[] = [
  'badPhone',
  'noPhone',
  'noName',
  'ambiguousService',
  'noService',
]

function sortHints(hints: readonly ImportHint[]): ImportHint[] {
  return [...new Set(hints)].sort(
    (left, right) => HINT_ORDER.indexOf(left) - HINT_ORDER.indexOf(right)
  )
}

/**
 * Только блокирующие коды и в естественном порядке.
 *
 * `past`/`tooFar`/`duplicate`/`overlap` считает `recomputeImportRows` после
 * правок, поэтому в черновике они появились бы дважды и не там.
 */
function sortIssues(issues: readonly ImportIssue[]): ImportIssue[] {
  return [...new Set(issues)].sort(
    (left, right) => ISSUE_ORDER.indexOf(left) - ISSUE_ORDER.indexOf(right)
  )
}

// ---------------------------------------------------------------------------
// Время.
// ---------------------------------------------------------------------------

function addMinutes(instant: string, minutes: number): string {
  return new Date(Date.parse(instant) + minutes * 60_000).toISOString()
}

function validEventDuration(value: number | null): number | null {
  if (value === null || value <= 0 || value > ICS_MAX_DURATION_MINUTES) {
    return null
  }

  return value
}

// ---------------------------------------------------------------------------
// Один черновик.
// ---------------------------------------------------------------------------

type ServiceLookup = {
  serviceId: string | null
  candidates: string[]
  issue: ImportIssue | null
}

/**
 * Услуга: `SUMMARY` → `CATEGORIES` → описание.
 *
 * Совпавший фрагмент вырезается из `SUMMARY` только тогда, когда найден именно
 * там: индексы `findService` относятся к переданной строке, и вырезание по ним из
 * другой строки съело бы часть имени. При равенстве совпадений `findService`
 * возвращает `ambiguous` — услугу не выбираем, но показываем кандидатов.
 */
function resolveService(
  sources: readonly { origin: 'summary' | 'categories' | 'description'; text: string }[],
  services: readonly BarberProfileService[]
): ServiceLookup & { summaryCut: { start: number; length: number } | null } {
  for (const source of sources) {
    const found = findService(source.text, services)

    if (found.match !== null) {
      return {
        serviceId: found.match.service.barberServiceId,
        candidates: [],
        issue: null,
        summaryCut:
          source.origin === 'summary'
            ? { start: found.match.start, length: found.match.length }
            : null,
      }
    }

    if (found.ambiguous.length > 0) {
      return {
        serviceId: null,
        candidates: found.ambiguous.map((service) => service.barberServiceId),
        issue: 'ambiguousService',
        summaryCut: null,
      }
    }
  }

  return { serviceId: null, candidates: [], issue: 'noService', summaryCut: null }
}

function mapOccurrence(
  occurrence: IcsOccurrence,
  line: number,
  options: IcsParseOptions
): ImportDraft {
  // Подпись строки — одна строка: теги заменяются пробелами, а лишние пробелы
  // схлопываются, иначе `<b>Стрижка</b> — …` показывалось бы с двойным пробелом.
  const plainSummary = collapse(
    toPlainText(occurrence.summary.slice(0, TEXT_LIMIT))
  ).slice(0, TEXT_LIMIT)
  const plainDescription = toPlainText(
    occurrence.description.slice(0, DESCRIPTION_LIMIT)
  )
  const plainLocation = toPlainText(occurrence.location.slice(0, TEXT_LIMIT))
  const plainCategories = toPlainText(occurrence.categories.slice(0, TEXT_LIMIT))

  // Для эвристик ссылки и встречи не текст: номер из ссылки — не телефон клиента,
  // а «Google Meet» в LOCATION — не салон.
  let summaryText = removeNoise(plainSummary)
  const descriptionText = removeNoise(plainDescription)
  const locationText = removeNoise(plainLocation)
  const categoriesText = removeNoise(plainCategories)

  const issues: ImportIssue[] = []
  const hints: ImportHint[] = []

  const phoneResult = resolvePhone([
    { origin: 'description', text: descriptionText },
    { origin: 'location', text: locationText },
    { origin: 'summary', text: summaryText },
  ])

  let phone: string | null = null

  if (phoneResult.hit !== null) {
    if (phoneResult.hit.issue !== null) {
      // Обрезанный номер не становится телефоном: в поле уходит только `+7…`,
      // иначе в бронь попала бы строка, которую сервер отвергнет.
      issues.push(phoneResult.hit.issue)
    } else {
      phone = phoneResult.hit.phone

      if (phoneResult.origin === 'description') {
        hints.push('phoneFromDescription')
      }
    }

    if (phoneResult.origin === 'summary') {
      summaryText = removeRange(
        summaryText,
        phoneResult.hit.start,
        phoneResult.hit.end
      )
    }
  }

  const service = resolveService(
    [
      { origin: 'summary', text: summaryText },
      { origin: 'categories', text: categoriesText },
      { origin: 'description', text: descriptionText },
    ],
    options.services
  )

  if (service.summaryCut !== null) {
    summaryText = removeRange(
      summaryText,
      service.summaryCut.start,
      service.summaryCut.start + service.summaryCut.length
    )
  }

  if (service.issue !== null) {
    issues.push(service.issue)
  }

  let salon: BarberProfileSalon | null = null

  if (locationText !== '' && !isMeetingLocation(plainLocation)) {
    const found = findSalonWithAddress(locationText, options.salons)

    if (found !== null) {
      salon = found.salon
    }
  }

  if (salon === null) {
    const found = findSalonWithAddress(summaryText, options.salons)

    if (found !== null) {
      salon = found.salon
      summaryText = removeRange(
        summaryText,
        found.start,
        found.start + found.length
      )
    }
  }

  if (salon === null) {
    const found = findSalonWithAddress(descriptionText, options.salons)

    if (found !== null) {
      salon = found.salon
    }
  }

  let salonId = salon?.salonId ?? null

  if (salonId === null) {
    salonId =
      options.defaultSalonId !== ''
        ? options.defaultSalonId
        : options.salons[0]?.salonId ?? null

    // При одном салоне подсказка бессмысленна: выбора всё равно нет.
    if (options.salons.length > 1) {
      hints.push('salonFallback')
    }
  }

  const selectedSalon =
    salon
    ?? options.salons.find((item) => item.salonId === salonId)
    ?? null

  const summaryName = splitName(cleanNameText(summaryText))
  const labeledName = summaryName === null
    ? findLabeledName(plainDescription)
    : null
  const name = summaryName
    ?? (labeledName === null ? null : splitName(labeledName))

  let firstName = name?.firstName ?? null
  let lastName = name?.lastName ?? null

  if (firstName === null) {
    issues.push('noName')
  }

  if (firstName !== null && firstName.length > NAME_LIMIT) {
    firstName = firstName.slice(0, NAME_LIMIT)
    hints.push('nameTruncated')
  }

  if (lastName !== null && lastName.length > NAME_LIMIT) {
    lastName = lastName.slice(0, NAME_LIMIT)
    hints.push('nameTruncated')
  }

  if (phone !== null && phone.length > PHONE_LIMIT) {
    phone = phone.slice(0, PHONE_LIMIT)
    hints.push('phoneTruncated')
  }

  // `badPhone` уже объясняет пустое поле: вторая ошибка про телефон не нужна.
  if (phone === null && !issues.includes('badPhone')) {
    issues.push('noPhone')
  }

  const eventDurationMinutes = validEventDuration(occurrence.durationMinutes)
  const selectedService = service.serviceId === null
    ? undefined
    : options.services.find(
      (item) => item.barberServiceId === service.serviceId
    )

  const startAt = occurrence.startAt
  const startMs = Date.parse(startAt)

  let endAt: string | null = null

  if (occurrence.endAt !== null && Date.parse(occurrence.endAt) > startMs) {
    endAt = occurrence.endAt
  } else if (eventDurationMinutes !== null) {
    endAt = addMinutes(startAt, eventDurationMinutes)
  } else if (
    selectedService !== undefined
    && selectedService.durationMinutes > 0
    && selectedService.durationMinutes <= ICS_MAX_DURATION_MINUTES
  ) {
    endAt = addMinutes(startAt, selectedService.durationMinutes)
  }

  if (
    eventDurationMinutes !== null
    && selectedService !== undefined
    && Math.abs(eventDurationMinutes - selectedService.durationMinutes)
      >= DURATION_MISMATCH_MINUTES
  ) {
    // Длительность события важнее: сервер не требует равенства услуге, а
    // подставить длительность услуги барбер сможет в предпросмотре.
    hints.push('durationMismatch')
  }

  const salonZone = selectedSalon?.timeZoneId ?? 'UTC'

  if (
    occurrence.sourceTimeZone !== null
    && occurrence.sourceTimeZone !== 'UTC'
    && occurrence.sourceTimeZone !== salonZone
  ) {
    hints.push('timeZoneConverted')
  }

  const personalText = collapse(flatten(`${plainSummary} ${plainDescription}`))

  if (PERSONAL_MARKERS.some((marker) => personalText.includes(marker))) {
    hints.push('personal')
  }

  if (occurrence.isFree) {
    hints.push('free')
  }

  if (occurrence.status === 'tentative') {
    hints.push('tentative')
  }

  const hasContact = phone !== null || service.serviceId !== null

  if (!hasContact) {
    hints.push('noContact')
  }

  // Личное событие и «свободно» — только мягкие подсказки: строку не удаляем.
  // Без телефона и услуги строку не отмечаем, но она остаётся в списке.
  const defaultIncluded = !hints.includes('free') && hasContact

  return {
    line,
    raw: plainSummary,
    date: salonDateFromInstant(startAt, salonZone),
    time: salonTimeFromInstant(startAt, salonZone),
    startAt,
    endAt,
    firstName,
    lastName,
    phone,
    salonId,
    serviceId: service.serviceId,
    serviceCandidates: service.candidates,
    issues: sortIssues(issues),
    source: 'ics',
    externalUid: occurrence.uid,
    recurrenceId: occurrence.recurrenceId,
    eventSummary: plainSummary,
    eventStartAt: occurrence.startAt,
    eventEndAt: occurrence.endAt,
    eventDurationMinutes,
    // Floating-событие не имеет абсолютного момента: его настенное время должно
    // пережить смену салона, поэтому строка считается от пояса салона.
    useEventTime: occurrence.sourceTimeZone !== null,
    sourceTimeZone: occurrence.sourceTimeZone,
    hints: sortHints(hints),
    defaultIncluded,
  }
}

/**
 * Вхождения календаря → черновики импорта, по одному на вхождение.
 *
 * Порядок вхождений сохраняется (парсер уже отсортировал их по времени), `line`
 * с 1 — по позиции в этом порядке. Ошибки `past`/`tooFar`/`duplicate`/`overlap`
 * сюда не входят: их считает `recomputeImportRows` после правок строки.
 */
export function mapIcsOccurrences(
  occurrences: readonly IcsOccurrence[],
  options: IcsParseOptions
): ImportDraft[] {
  return occurrences.map((occurrence, index) =>
    mapOccurrence(occurrence, index + 1, options)
  )
}
