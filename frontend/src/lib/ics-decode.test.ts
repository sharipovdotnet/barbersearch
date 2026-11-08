import { describe, expect, it, vi } from 'vitest'

import {
  decodeIcsFile,
  ICS_MAX_FILE_BYTES,
  type IcsFileLike,
} from '@/lib/ics-decode'

/**
 * Декодер — первая линия обороны импорта: сюда приходит недоверенный файл,
 * поэтому проверяем и удачные кодировки, и все отказы контракта.
 *
 * Файлы подменяем объектами `{ size, arrayBuffer }`: декодеру больше ничего не
 * нужно, а jsdom не обязан поддерживать `File.arrayBuffer`.
 */

const encoder = new TextEncoder()

const CALENDAR_TEXT = [
  'BEGIN:VCALENDAR',
  'VERSION:2.0',
  'PRODID:-//BarberSearch//Test//RU',
  'BEGIN:VEVENT',
  'UID:event-1@barbersearch',
  'DTSTART:20261112T100000Z',
  'SUMMARY:Иван Петров',
  'END:VEVENT',
  'END:VCALENDAR',
].join('\n')

/** UTF-8 байты, при желании с BOM. */
function utf8Bytes(text: string, withBom = false): Uint8Array {
  const body = encoder.encode(text)

  if (!withBom) {
    return body
  }

  const bytes = new Uint8Array(body.length + 3)
  bytes.set([0xef, 0xbb, 0xbf], 0)
  bytes.set(body, 3)

  return bytes
}

/** UTF-16 байты с BOM нужного порядка: `TextEncoder` умеет только UTF-8. */
function utf16Bytes(text: string, littleEndian: boolean): Uint8Array {
  const bytes = new Uint8Array(2 + text.length * 2)
  bytes[0] = littleEndian ? 0xff : 0xfe
  bytes[1] = littleEndian ? 0xfe : 0xff

  for (let index = 0; index < text.length; index += 1) {
    const code = text.charCodeAt(index)
    bytes[2 + index * 2] = littleEndian ? code & 0xff : code >> 8
    bytes[3 + index * 2] = littleEndian ? code >> 8 : code & 0xff
  }

  return bytes
}

/** Файл-двойник: декодеру нужны только `size` и `arrayBuffer`. */
function fakeFile(bytes: Uint8Array): IcsFileLike {
  const copy = new Uint8Array(bytes)

  return {
    size: copy.byteLength,
    arrayBuffer: async () => copy.buffer,
  }
}

describe('decodeIcsFile: кодировки', () => {
  it('UTF-8 без BOM возвращает текст как есть', async () => {
    await expect(decodeIcsFile(fakeFile(utf8Bytes(CALENDAR_TEXT)))).resolves.toEqual(
      { text: CALENDAR_TEXT, failure: null }
    )
  })

  it('UTF-8 BOM срезается и не попадает в текст', async () => {
    await expect(
      decodeIcsFile(fakeFile(utf8Bytes(CALENDAR_TEXT, true)))
    ).resolves.toEqual({ text: CALENDAR_TEXT, failure: null })
  })

  it('UTF-16LE с BOM декодируется как UTF-16LE', async () => {
    await expect(
      decodeIcsFile(fakeFile(utf16Bytes(CALENDAR_TEXT, true)))
    ).resolves.toEqual({ text: CALENDAR_TEXT, failure: null })
  })

  it('UTF-16BE с BOM декодируется как UTF-16BE', async () => {
    await expect(
      decodeIcsFile(fakeFile(utf16Bytes(CALENDAR_TEXT, false)))
    ).resolves.toEqual({ text: CALENDAR_TEXT, failure: null })
  })

  it('битый UTF-8 даёт invalidEncoding и не бросает исключение', async () => {
    // 0xc3 и 0x28 — недопустимая последовательность UTF-8 (нет второго байта
    // продолжения), 0xff не встречается в UTF-8 вообще.
    const broken = new Uint8Array([0x42, 0x45, 0x47, 0x49, 0x4e, 0xc3, 0x28, 0xff])

    await expect(decodeIcsFile(fakeFile(broken))).resolves.toEqual({
      text: '',
      failure: 'invalidEncoding',
    })
  })
})

describe('decodeIcsFile: лимит размера', () => {
  it('файл больше 2 МБ — tooLarge, arrayBuffer не вызывается', async () => {
    const arrayBuffer = vi.fn(async () => new ArrayBuffer(0))

    const result = await decodeIcsFile({
      size: ICS_MAX_FILE_BYTES + 1,
      arrayBuffer,
    })

    expect(result).toEqual({ text: '', failure: 'tooLarge' })
    expect(arrayBuffer).not.toHaveBeenCalled()
  })

  it('файл ровно 2 МБ ещё читается: лимит строгий', async () => {
    const result = await decodeIcsFile({
      size: ICS_MAX_FILE_BYTES,
      arrayBuffer: async () => new Uint8Array(utf8Bytes(CALENDAR_TEXT)).buffer,
    })

    expect(result).toEqual({ text: CALENDAR_TEXT, failure: null })
  })
})

describe('decodeIcsFile: не календарь', () => {
  it('HTML с расширением .ics — notCalendar', async () => {
    const html = '<!DOCTYPE html>\n<html><body>Это не календарь</body></html>\n'

    await expect(decodeIcsFile(fakeFile(utf8Bytes(html)))).resolves.toEqual({
      text: '',
      failure: 'notCalendar',
    })
  })

  it('CSV — notCalendar', async () => {
    const csv = 'name,phone,date\nИван,79990000000,2026-11-12\n'

    await expect(decodeIcsFile(fakeFile(utf8Bytes(csv)))).resolves.toEqual({
      text: '',
      failure: 'notCalendar',
    })
  })

  it('нижний регистр BEGIN:vcalendar — notCalendar: формат регистрозависим', async () => {
    const lower = CALENDAR_TEXT.replace('BEGIN:VCALENDAR', 'begin:vcalendar')

    await expect(decodeIcsFile(fakeFile(utf8Bytes(lower)))).resolves.toEqual({
      text: '',
      failure: 'notCalendar',
    })
  })
})

describe('decodeIcsFile: пустой ввод', () => {
  it('пустой файл — empty', async () => {
    await expect(decodeIcsFile(fakeFile(new Uint8Array(0)))).resolves.toEqual({
      text: '',
      failure: 'empty',
    })
  })

  it('только пробелы и переводы строк — empty', async () => {
    await expect(
      decodeIcsFile(fakeFile(utf8Bytes('  \r\n\r\n\t\n   ')))
    ).resolves.toEqual({ text: '', failure: 'empty' })
  })

  it('BOM и пустые строки без календаря — empty, а не notCalendar', async () => {
    await expect(
      decodeIcsFile(fakeFile(utf8Bytes('\n\n   \n', true)))
    ).resolves.toEqual({ text: '', failure: 'empty' })
  })
})

describe('decodeIcsFile: структура файла', () => {
  it('пустые строки и BOM перед BEGIN:VCALENDAR — успех', async () => {
    const padded = `\n\n   \n\r\n${CALENDAR_TEXT}`

    const result = await decodeIcsFile(fakeFile(utf8Bytes(padded, true)))

    // Ведущие пустые строки — не часть формата: текст начинается с заголовка,
    // чтобы ical.js не спотыкался о них.
    expect(result).toEqual({ text: CALENDAR_TEXT, failure: null })
    expect(result.text.startsWith('BEGIN:VCALENDAR')).toBe(true)
  })

  it('бинарный NUL — invalid', async () => {
    const binary = 'BEGIN:VCALENDAR\nX\0Y\nEND:VCALENDAR'

    await expect(decodeIcsFile(fakeFile(utf8Bytes(binary)))).resolves.toEqual({
      text: '',
      failure: 'invalid',
    })
  })

  it('два BEGIN:VCALENDAR в одном файле — invalid', async () => {
    const twoCalendars = `${CALENDAR_TEXT}\n${CALENDAR_TEXT}`

    await expect(
      decodeIcsFile(fakeFile(utf8Bytes(twoCalendars)))
    ).resolves.toEqual({ text: '', failure: 'invalid' })
  })

  it('нет END:VCALENDAR — invalid: файл обрезан', async () => {
    const truncated = CALENDAR_TEXT.split('\n').slice(0, -1).join('\n')

    await expect(decodeIcsFile(fakeFile(utf8Bytes(truncated)))).resolves.toEqual(
      { text: '', failure: 'invalid' }
    )
  })
})

describe('decodeIcsFile: нормализация текста', () => {
  it('\\r\\n и одиночный \\r превращаются в \\n', async () => {
    const windows = 'BEGIN:VCALENDAR\r\nVERSION:2.0\rSUMMARY:Тест\r\nEND:VCALENDAR'

    const result = await decodeIcsFile(fakeFile(utf8Bytes(windows)))

    expect(result).toEqual({
      text: 'BEGIN:VCALENDAR\nVERSION:2.0\nSUMMARY:Тест\nEND:VCALENDAR',
      failure: null,
    })
    expect(result.text).not.toContain('\r')
  })
})
