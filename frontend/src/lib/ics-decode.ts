/**
 * Чтение выгруженного календаря: байты → нормализованный текст.
 *
 * Файл разбирается целиком в браузере и никуда не загружается, поэтому здесь
 * только дешёвые проверки: размер, кодировка, отсутствие бинарного мусора и
 * обязательный `BEGIN:VCALENDAR`. Всё, что касается событий, живёт в `ics-parse`.
 */

export const ICS_MAX_FILE_BYTES = 2 * 1024 * 1024

/** Минимальная форма `File`, которую реально использует декодер. */
export type IcsFileLike = {
  readonly size: number
  arrayBuffer(): Promise<ArrayBuffer>
}

export type IcsDecodeFailure =
  | 'tooLarge'
  | 'invalidEncoding'
  | 'notCalendar'
  | 'invalid'
  | 'empty'
  | null

export type IcsDecodeResult = {
  /** Нормализованный текст календаря; `''` при ошибке. */
  text: string
  failure: IcsDecodeFailure
}

/** Байты BOM: UTF-8 и оба порядка UTF-16. */
const UTF8_BOM = [0xef, 0xbb, 0xbf] as const
const UTF16LE_BOM = [0xff, 0xfe] as const
const UTF16BE_BOM = [0xfe, 0xff] as const

/**
 * C0-контролы, которых не бывает в текстовом календаре.
 *
 * Табуляция, перевод строки и возврат каретки разрешены: они встречаются в
 * `DESCRIPTION` и в переносе строк RFC 5545. Контрольные символы в регулярке —
 * это и есть предмет проверки, поэтому предупреждение линтера снимаем точечно.
 */
// oxlint-disable-next-line no-control-regex
const BINARY_CONTROL_CHARS = /[\u0000-\u0008\u000B\u000C\u000E-\u001F]/

const CALENDAR_START = 'BEGIN:VCALENDAR'
const CALENDAR_END = 'END:VCALENDAR'

function fail(failure: Exclude<IcsDecodeFailure, null>): IcsDecodeResult {
  return { text: '', failure }
}

function startsWithBytes(bytes: Uint8Array, prefix: readonly number[]): boolean {
  if (bytes.length < prefix.length) {
    return false
  }

  return prefix.every((byte, index) => bytes[index] === byte)
}

/**
 * Декодирует буфер с учётом BOM.
 *
 * BOM проверяем первым: без него UTF-16 легко принять за «битый UTF-8», а
 * UTF-8 BOM, попавший в текст, ломает точное сравнение первой строки с
 * `BEGIN:VCALENDAR`. `fatal: true` нужен, чтобы битые байты не превратились в
 * текст с символами замены: такой файл лучше показать ошибкой, чем разобрать
 * наполовину.
 */
function decodeWithBom(bytes: Uint8Array): string {
  if (startsWithBytes(bytes, UTF8_BOM)) {
    return new TextDecoder('utf-8', { fatal: true }).decode(
      bytes.subarray(UTF8_BOM.length)
    )
  }

  if (startsWithBytes(bytes, UTF16LE_BOM)) {
    return new TextDecoder('utf-16le', { fatal: true }).decode(bytes)
  }

  if (startsWithBytes(bytes, UTF16BE_BOM)) {
    return new TextDecoder('utf-16be', { fatal: true }).decode(bytes)
  }

  return new TextDecoder('utf-8', { fatal: true }).decode(bytes)
}

/**
 * Читает `.ics` и возвращает нормализованный текст либо код ошибки.
 *
 * MIME и расширение не проверяем: браузер отдаёт `text/calendar` и для HTML,
 * который сервер объявил календарём, а расширение пользователь переименовывает
 * как угодно. Доверять можно только содержимому, поэтому календарь опознаётся по
 * первой содержательной строке.
 */
export async function decodeIcsFile(
  file: IcsFileLike
): Promise<IcsDecodeResult> {
  // Лимит — до чтения: незачем тянуть в память вкладки файл, который всё равно
  // отклонён. `size` у `File` известен без чтения.
  if (file.size > ICS_MAX_FILE_BYTES) {
    return fail('tooLarge')
  }

  // Ошибку самого чтения не превращаем в код: у неё нет своего значения в
  // контракте, и вызывающий код должен увидеть настоящую причину.
  const bytes = new Uint8Array(await file.arrayBuffer())

  let text: string

  try {
    text = decodeWithBom(bytes)
  } catch {
    // `fatal: true` бросает на любой невалидной последовательности; наружу
    // исключение не отдаём — это ожидаемый исход «файл не в UTF-8/UTF-16».
    return fail('invalidEncoding')
  }

  // UTF-16-декодер срезает свой BOM сам, но удвоенный BOM (`FF FE FF FE`) или
  // BOM, пришедший в тексте иным путём, оставил бы `\uFEFF` перед `BEGIN`.
  // Срезаем ровно один ведущий символ, чтобы не испортить данные.
  if (text.startsWith('\uFEFF')) {
    text = text.slice(1)
  }

  if (text.trim() === '') {
    return fail('empty')
  }

  // Проверка NUL — после декодирования: в UTF-16 каждый ASCII-символ занимает
  // два байта, и один из них всегда `00`. Проверять байты до декодирования
  // значило бы объявить бинарным любой корректный UTF-16-файл.
  if (BINARY_CONTROL_CHARS.test(text)) {
    return fail('invalid')
  }

  // Переносы строк приводим к `\n` до разбиения на строки: `.ics` после Windows
  // и старых Apple-клиентов приходит с `\r\n` или одиночным `\r`, а сравнение с
  // `BEGIN:VCALENDAR` должно быть предсказуемым.
  const normalized = text.replace(/\r\n?/g, '\n')
  const lines = normalized.split('\n')

  let firstContentLine = 0

  while (
    firstContentLine < lines.length &&
    lines[firstContentLine].trim() === ''
  ) {
    firstContentLine += 1
  }

  if (firstContentLine === lines.length) {
    return fail('empty')
  }

  // Сравнение точное и регистрозависимое: RFC 5545 требует верхний регистр, а
  // молчаливое `begin:vcalendar` скрыло бы файл, который ical.js всё равно не
  // разберёт.
  if (lines[firstContentLine] !== CALENDAR_START) {
    return fail('notCalendar')
  }

  // Один VCALENDAR на файл: несколько календарей в одном файле — это Takeout или
  // ручная склейка. Экспорт Google/Apple/Outlook всегда даёт один календарь,
  // поэтому склейку в v1 не поддерживаем и просим выгрузить один календарь.
  if (lines.filter((line) => line === CALENDAR_START).length > 1) {
    return fail('invalid')
  }

  // Незакрытый календарь — обрезанный файл: ical.js разберёт его частично, и
  // барбер увидит неполный список, ничего не заподозрив.
  if (!lines.includes(CALENDAR_END)) {
    return fail('invalid')
  }

  // Пустые строки до `BEGIN:VCALENDAR` — не часть формата: убираем их, чтобы
  // разбор начинался с заголовка календаря.
  return { text: lines.slice(firstContentLine).join('\n'), failure: null }
}
