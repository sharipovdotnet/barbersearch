import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import type { ImportIssue } from '@/lib/import-parse'

/**
 * Общие матчеры импорта: телефон, услуга, салон и имя.
 *
 * Вынесены из `import-parse.ts`, чтобы текстовый и календарный импорты находили
 * «Стрижку» одинаково: второй набор эвристик рано или поздно разошёлся бы с первым.
 * Чистые функции без сети, React и состояния.
 */

const NON_WORD = /[^\p{L}\p{N}]+/gu
const SINGLE_NON_WORD = /[^\p{L}\p{N}]/gu

/** Служебные слова в остатке строки — не часть имени. */
const NAME_STOP_WORDS = new Set(['клиент', 'клиентка', 'запись', 'в', 'на', 'к', 'для'])

/**
 * Стандартный телефон с группировкой 3-3-2-2.
 *
 * Форматы: `+7 999 123-45-67`, `8 (999) 123-45-67`, `79991234567`, `9991234567`.
 * Границы цифр не дают выхватить телефон из середины более длинного числа.
 */
const PHONE_PATTERN =
  /(?<!\d)(?:\+\s?7|[78])?\s*[-(]?\s*\d{3}\s*[)-]?\s*\d{3}\s*[-\s]?\s*\d{2}\s*[-\s]?\s*\d{2}(?!\d)/

/** Пробельная последовательность цифр — второй шанс для нестандартной группировки. */
const DIGIT_RUN = /\d[\d+()\-\s]*/g

/** Начало телефона: цифр не хватает или их слишком много — «похоже на неполный». */
const PHONE_START = /(?<!\d)(?:\+\s?7|[78])[\s\-()]*\d[\d\s\-()]*/g

/** Нормализованный гость: `+7` и десять цифр. */
const IMPORT_PHONE = /^\+7\d{10}$/

/**
 * Текст для сравнения: нижний регистр, `ё → е`, знаки — пробелы.
 *
 * Замена посимвольная, поэтому длина совпадает с исходной строкой и найденный
 * фрагмент можно вырезать из оригинала по тем же индексам. `ё → е` нужно только
 * здесь: имя клиента печатается как записано.
 */
export function flatten(value: string): string {
  return value.toLowerCase().replace(/ё/g, 'е').replace(SINGLE_NON_WORD, ' ')
}

export function collapse(value: string): string {
  return value.replace(/\s+/g, ' ').trim()
}

export function removeRange(value: string, start: number, end: number): string {
  return `${value.slice(0, start)} ${value.slice(end)}`
}

/** Нормализация телефона по числу цифр: `8…` и `7…` → `+7…`, десять цифр → `+7…`. */
export function normalizePhone(value: string): string | null {
  const digits = value.replace(/\D/g, '')

  if (digits.length === 10) {
    return `+7${digits}`
  }

  if (digits.length === 11 && (digits[0] === '7' || digits[0] === '8')) {
    return `+7${digits.slice(1)}`
  }

  return null
}

/** Найденный телефон или «похоже на неполный» с его местом в строке. */
export type PhoneHit = {
  phone: string | null
  issue: ImportIssue | null
  /** Токен, который не удалось нормализовать: показываем как есть. */
  raw: string | null
  start: number
  end: number
}

/**
 * Поиск телефона до разбора времени.
 *
 * Три прохода: строгая маска, затем десятизначная пробельная серия, затем
 * «похоже на неполный». Обратный порядок съел бы время, а без третьего прохода
 * обрезанный номер молча превратился бы в имя.
 */
export function findPhone(text: string): PhoneHit | null {
  const strict = PHONE_PATTERN.exec(text)

  if (strict !== null) {
    const phone = normalizePhone(strict[0])

    if (phone !== null) {
      return {
        phone,
        issue: null,
        raw: null,
        start: strict.index,
        end: strict.index + strict[0].length,
      }
    }
  }

  for (const match of text.matchAll(DIGIT_RUN)) {
    const snippet = match[0].trimEnd()
    const phone = normalizePhone(snippet)

    if (phone === null) {
      continue
    }

    return {
      phone,
      issue: null,
      raw: null,
      start: match.index,
      end: match.index + snippet.length,
    }
  }

  for (const match of text.matchAll(PHONE_START)) {
    const snippet = match[0].trimEnd()

    if (snippet.replace(/\D/g, '').length < 7) {
      continue
    }

    return {
      phone: null,
      issue: 'badPhone',
      raw: snippet,
      start: match.index,
      end: match.index + snippet.length,
    }
  }

  return null
}

/** Телефон в том виде, в котором его отправит `POST api/barber/bookings`. */
export function isImportPhone(value: string): boolean {
  return IMPORT_PHONE.test(value)
}

/** Правка телефона из предпросмотра: `8…`, `7…` и десять цифр сводятся к `+7…`. */
export function normalizeImportPhone(value: string): string | null {
  const digits = value.replace(/\D/g, '')

  if (digits.length !== 10 && digits.length !== 11) {
    return null
  }

  return normalizePhone(digits)
}

/** Тир совпадения: точное, с начала строки, внутри неё. */
export type MatchTier = 0 | 1 | 2

export type ServiceMatch = {
  service: BarberProfileService
  tier: MatchTier
  start: number
  length: number
}

/**
 * Совпадение названия с остатком строки: точное, с начала, внутри.
 *
 * Название услуги превращается в последовательность слов, поэтому «стрижка»
 * находится и в «стрижка бороды», а пунктуация не мешает. Индексы относятся
 * к `flatten`-представлению, длина которого совпадает с оригиналом.
 */
export function matchName(
  text: string,
  name: string
): { tier: MatchTier; start: number; length: number } | null {
  const normalized = collapse(flatten(name))

  if (normalized === '') {
    return null
  }

  const flat = flatten(text)
  const words = normalized
    .split(' ')
    .map((word) => word.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  const pattern = new RegExp(
    `(?<![\\p{L}\\p{N}])${words.join('[^\\p{L}\\p{N}]+')}(?![\\p{L}\\p{N}])`,
    'gu'
  )
  const match = pattern.exec(flat)

  if (match === null) {
    return null
  }

  const before = flat.slice(0, match.index).trim()
  const after = flat.slice(match.index + match[0].length).trim()
  const tier: MatchTier = before === '' && after === '' ? 0 : before === '' ? 1 : 2

  return { tier, start: match.index, length: match[0].length }
}

/** Лучший кандидат: сначала тир, затем самое длинное совпадение. */
export function pickBest<T>(
  candidates: readonly { value: T; tier: MatchTier; start: number; length: number }[]
): { value: T; start: number; length: number } | null {
  let best: { value: T; start: number; length: number; tier: MatchTier } | null = null

  for (const candidate of candidates) {
    if (
      best === null
      || candidate.tier < best.tier
      || (candidate.tier === best.tier && candidate.length > best.length)
    ) {
      best = candidate
    }
  }

  return best === null ? null : { value: best.value, start: best.start, length: best.length }
}

export function findService(
  text: string,
  services: readonly BarberProfileService[]
): { match: ServiceMatch | null; ambiguous: BarberProfileService[] } {
  const matches: ServiceMatch[] = []

  for (const service of services) {
    if (!service.isActive) {
      continue
    }

    const hit = matchName(text, service.name)

    if (hit !== null) {
      matches.push({ service, ...hit })
    }
  }

  if (matches.length === 0) {
    return { match: null, ambiguous: [] }
  }

  const best = pickBest(matches.map((match) => ({
    value: match,
    tier: match.tier,
    start: match.start,
    length: match.length,
  })))

  if (best === null) {
    return { match: null, ambiguous: [] }
  }

  const tied = matches.filter(
    (match) => match.tier === best.value.tier && match.length === best.length
  )

  if (tied.length > 1) {
    return { match: null, ambiguous: tied.map((match) => match.service) }
  }

  return { match: best.value, ambiguous: [] }
}

export function findSalon(
  text: string,
  salons: readonly BarberProfileSalon[]
): { salon: BarberProfileSalon; start: number; length: number } | null {
  const candidates: { value: BarberProfileSalon; tier: MatchTier; start: number; length: number }[] = []

  for (const salon of salons) {
    for (const name of [salon.name, salon.city]) {
      if (name.trim() === '') {
        continue
      }

      const hit = matchName(text, name)

      if (hit !== null) {
        candidates.push({ value: salon, ...hit })
        break
      }
    }
  }

  const best = pickBest(candidates)

  return best === null ? null : { salon: best.value, start: best.start, length: best.length }
}

/** Остаток строки → имя и фамилия: пунктуация и служебные слова отбрасываются. */
export function splitName(
  rest: string
): { firstName: string; lastName: string | null } | null {
  const words = rest
    .replace(NON_WORD, ' ')
    .split(' ')
    .filter((word) => word !== '')
    .filter((word) => !NAME_STOP_WORDS.has(word.toLowerCase()))
    .filter((word) => !/^\d+$/.test(word))

  if (words.length === 0) {
    return null
  }

  const firstName = words[0]

  if (words.length === 1) {
    return { firstName, lastName: null }
  }

  return { firstName, lastName: words[words.length - 1] }
}
