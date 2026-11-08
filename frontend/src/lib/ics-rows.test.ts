import { describe, expect, it } from 'vitest'

import {
  applyImportRowPatch,
  findOverlapPartner,
  parseImportText,
  recomputeImportRows,
  type ImportDraft,
  type ImportParseOptions,
} from '@/lib/import-parse'
import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'

/**
 * Пересчёт строк календаря: момент события, окно сервера, дубли и пересечения.
 *
 * `now` зафиксирован: 8 ноября 2026, 15:00 UTC — это 20:00 в Екатеринбурге.
 * События лежат 12 ноября, поэтому проверки «прошлое» и «дальше 90 дней»
 * отделены от обычных строк.
 */

const NOW = Date.parse('2026-11-08T15:00:00Z')
const TODAY = '2026-11-08'
const HOUR_MS = 60 * 60 * 1000
const DAY_MS = 24 * HOUR_MS
/** Верхняя граница окна с часовым запасом: `now + 90 дней − 1 час`. */
const FAR_BOUNDARY_MS = NOW + 90 * DAY_MS - HOUR_MS

const leninSalon: BarberProfileSalon = {
  id: 'profile-salon-s1',
  salonId: 's1',
  name: 'Ленина',
  address: 'Ленина 1',
  city: 'Екатеринбург',
  country: 'Russia',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon',
  isPrimary: true,
  workingSchedules: [],
}

const berlinSalon: BarberProfileSalon = {
  id: 'profile-salon-s2',
  salonId: 's2',
  name: 'Берлин',
  address: 'Берлин 1',
  city: 'Берлин',
  country: 'Germany',
  timeZoneId: 'Europe/Berlin',
  kind: 'Salon',
  isPrimary: false,
  workingSchedules: [],
}

const haircut: BarberProfileService = {
  barberServiceId: 'service-haircut',
  hairServiceId: 'hair-haircut',
  name: 'Стрижка',
  category: 'Haircut',
  defaultDurationMinutes: 60,
  durationMinutes: 60,
  price: 2000,
  isActive: true,
}

const options: ImportParseOptions = {
  services: [haircut],
  salons: [leninSalon, berlinSalon],
  today: TODAY,
  now: NOW,
  defaultSalonId: leninSalon.salonId,
}

/** Готовая текстовая строка: от неё строятся фикстуры календаря. */
function baseDraft(): ImportDraft {
  return {
    line: 1,
    raw: '',
    date: '2026-11-12',
    time: '15:00',
    startAt: null,
    endAt: null,
    firstName: 'Иван',
    lastName: 'Петров',
    phone: '+79991234567',
    salonId: leninSalon.salonId,
    serviceId: haircut.barberServiceId,
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

function textRow(overrides: Partial<ImportDraft> = {}): ImportDraft {
  return { ...baseDraft(), ...overrides }
}

function icsRow(overrides: Partial<ImportDraft> = {}): ImportDraft {
  return {
    ...baseDraft(),
    source: 'ics',
    externalUid: 'uid-1',
    eventSummary: 'Стрижка',
    eventStartAt: '2026-11-12T10:00:00.000Z',
    eventEndAt: '2026-11-12T11:00:00.000Z',
    eventDurationMinutes: 60,
    useEventTime: true,
    sourceTimeZone: 'Asia/Yekaterinburg',
    ...overrides,
  }
}

describe('recomputeImportRows: время ICS-строки', () => {
  it('сохраняет startAt/endAt события и не сбрасывает дату и время', () => {
    const [next] = recomputeImportRows([icsRow()], options)

    expect(next.startAt).toBe('2026-11-12T10:00:00.000Z')
    expect(next.endAt).toBe('2026-11-12T11:00:00.000Z')
    expect(next.date).toBe('2026-11-12')
    expect(next.time).toBe('15:00')
    expect(next.useEventTime).toBe(true)
    expect(next.issues).toStrictEqual([])
    expect(next.hints).toStrictEqual([])
  })

  it('правка даты и времени выключает время события и считает момент в поясе салона', () => {
    const patched = applyImportRowPatch(
      icsRow({ eventDurationMinutes: 45 }),
      { date: '2026-11-13', time: '12:00' }
    )

    expect(patched.useEventTime).toBe(false)

    const [next] = recomputeImportRows([patched], options)

    expect(next.useEventTime).toBe(false)
    expect(next.date).toBe('2026-11-13')
    expect(next.time).toBe('12:00')
    expect(next.startAt).toBe('2026-11-13T07:00:00.000Z')
    expect(next.endAt).toBe('2026-11-13T07:45:00.000Z')
    expect(next.eventDurationMinutes).toBe(45)
    expect(next.issues).toStrictEqual([])
  })

  it('правка салона сохраняет момент и пересчитывает дату и время в новом поясе', () => {
    const patched = applyImportRowPatch(icsRow(), {
      salonId: berlinSalon.salonId,
    })

    expect(patched.useEventTime).toBe(true)
    expect(patched.time).toBe('15:00')

    const [next] = recomputeImportRows([patched], options)

    expect(next.startAt).toBe('2026-11-12T10:00:00.000Z')
    expect(next.endAt).toBe('2026-11-12T11:00:00.000Z')
    expect(next.date).toBe('2026-11-12')
    expect(next.time).toBe('11:00')
    expect(next.time).not.toBe(patched.time)
  })

  it('подстановка слота сбрасывает длительность события и берёт длительность услуги', () => {
    const patched = applyImportRowPatch(
      icsRow({ eventDurationMinutes: 45 }),
      {
        date: '2026-11-14',
        time: '09:30',
        useEventTime: false,
        eventDurationMinutes: null,
      }
    )

    const [next] = recomputeImportRows([patched], options)

    expect(next.eventDurationMinutes).toBeNull()
    expect(next.startAt).toBe('2026-11-14T04:30:00.000Z')
    expect(next.endAt).toBe('2026-11-14T05:30:00.000Z')
    expect(next.issues).toStrictEqual([])
    expect(next.hints).toStrictEqual([])
  })

  it('берёт конец события при длительности 45 минут, хотя услуга длится 60', () => {
    const [next] = recomputeImportRows(
      [
        icsRow({
          eventEndAt: '2026-11-12T10:45:00.000Z',
          eventDurationMinutes: 45,
        }),
      ],
      options
    )

    expect(next.startAt).toBe('2026-11-12T10:00:00.000Z')
    expect(next.endAt).toBe('2026-11-12T10:45:00.000Z')
    expect(next.issues).toStrictEqual([])
    expect(next.hints).toStrictEqual([])
  })

  it('невалидная длительность события падает на услугу и добавляет подсказку', () => {
    const [next] = recomputeImportRows(
      [
        icsRow({
          eventEndAt: null,
          eventDurationMinutes: 721,
          hints: ['timeZoneConverted'],
        }),
      ],
      options
    )

    expect(next.startAt).toBe('2026-11-12T10:00:00.000Z')
    expect(next.endAt).toBe('2026-11-12T11:00:00.000Z')
    expect(next.issues).toStrictEqual([])
    expect(next.hints).toStrictEqual(['timeZoneConverted', 'durationMismatch'])
  })

  it('начало дальше 90 дней — tooFar', () => {
    const [next] = recomputeImportRows(
      [
        icsRow({
          eventStartAt: '2027-03-01T10:00:00.000Z',
          eventEndAt: null,
          date: '2027-03-01',
        }),
      ],
      options
    )

    expect(next.startAt).toBe('2027-03-01T10:00:00.000Z')
    expect(next.issues).toStrictEqual(['tooFar'])
  })

  it('начало ровно на границе 90 дней минус час — ещё не tooFar', () => {
    const boundary = new Date(FAR_BOUNDARY_MS).toISOString()
    const [next] = recomputeImportRows(
      [
        icsRow({
          eventStartAt: boundary,
          eventEndAt: null,
          date: '2027-02-06',
        }),
      ],
      options
    )

    expect(next.startAt).toBe(boundary)
    expect(next.issues).toStrictEqual([])
  })

  it('начало на миллисекунду позже границы — tooFar', () => {
    const boundary = new Date(FAR_BOUNDARY_MS + 1).toISOString()
    const [next] = recomputeImportRows(
      [
        icsRow({
          eventStartAt: boundary,
          eventEndAt: null,
          date: '2027-02-06',
        }),
      ],
      options
    )

    expect(next.startAt).toBe(boundary)
    expect(next.issues).toStrictEqual(['tooFar'])
  })

  it('начало больше чем за пять минут до now — past', () => {
    const [next] = recomputeImportRows(
      [
        icsRow({
          eventStartAt: '2026-11-08T14:50:00.000Z',
          eventEndAt: null,
          date: '2026-11-08',
        }),
      ],
      options
    )

    expect(next.issues).toStrictEqual(['past'])
  })
})

describe('recomputeImportRows: пересечения ICS-строк', () => {
  const first = icsRow({ line: 1, externalUid: 'uid-a' })
  const second = icsRow({
    line: 2,
    externalUid: 'uid-b',
    eventStartAt: '2026-11-12T10:30:00.000Z',
    eventEndAt: '2026-11-12T11:30:00.000Z',
    time: '15:30',
  })

  it('помечает пересечение у второй строки, но не у первой', () => {
    const next = recomputeImportRows([first, second], options)

    expect(next[0].issues).toStrictEqual([])
    expect(next[1].issues).toStrictEqual(['overlap'])
  })

  it('findOverlapPartner возвращает раннюю строку для поздней', () => {
    const next = recomputeImportRows([first, second], options)

    expect(findOverlapPartner(next, second.line)).toBe(next[0])
    expect(findOverlapPartner(next, first.line)).toBeNull()
  })

  it('сдвиг времени снимает пересечение при пересчёте', () => {
    const moved = applyImportRowPatch(second, {
      date: '2026-11-12',
      time: '13:00',
    })
    const next = recomputeImportRows([first, moved], options)

    expect(next[1].useEventTime).toBe(false)
    expect(next[1].issues).toStrictEqual([])
  })

  it('исключённая строка не создаёт и не получает пересечений', () => {
    const third = icsRow({
      line: 3,
      externalUid: 'uid-c',
      eventStartAt: '2026-11-12T11:00:00.000Z',
      eventEndAt: '2026-11-12T12:00:00.000Z',
      time: '16:00',
    })
    const included = new Set([1, 3])
    const next = recomputeImportRows([first, second, third], options, included)

    expect(next[1].issues).toStrictEqual([])
    expect(next[2].issues).toStrictEqual([])
    expect(findOverlapPartner(next, third.line, included)).toBeNull()
  })

  it('третья строка пересекает только вторую и тоже помечается', () => {
    const third = icsRow({
      line: 3,
      externalUid: 'uid-c',
      eventStartAt: '2026-11-12T11:00:00.000Z',
      eventEndAt: '2026-11-12T12:00:00.000Z',
      time: '16:00',
    })
    const next = recomputeImportRows([first, second, third], options)

    expect(next[0].issues).toStrictEqual([])
    expect(next[1].issues).toStrictEqual(['overlap'])
    expect(next[2].issues).toStrictEqual(['overlap'])
    expect(findOverlapPartner(next, third.line)).toBe(next[1])
  })
})

describe('recomputeImportRows: дубли', () => {
  it('одинаковое время и телефон — у второй строки duplicate', () => {
    const next = recomputeImportRows(
      [
        icsRow({ line: 1, externalUid: 'uid-a' }),
        icsRow({ line: 2, externalUid: 'uid-b', firstName: 'Пётр' }),
      ],
      options
    )

    expect(next[0].issues).toStrictEqual([])
    expect(next[1].issues).toContain('duplicate')
  })

  it('одинаковое время, но разные телефоны — не дубль', () => {
    const next = recomputeImportRows(
      [
        icsRow({ line: 1, externalUid: 'uid-a' }),
        icsRow({
          line: 2,
          externalUid: 'uid-b',
          phone: '+79990003344',
          firstName: 'Пётр',
        }),
      ],
      options
    )

    expect(next[1].issues).not.toContain('duplicate')
  })

  it('текстовые строки сохраняют прежнее правило дубля', () => {
    const result = parseImportText(
      [
        '12 ноября',
        '10:00 Иван стрижка +79990001122',
        '10:00 Пётр стрижка +79990003344',
      ].join('\n'),
      options
    )

    expect(result.failure).toBeNull()
    expect(result.rows.map((row) => row.issues)).toStrictEqual([
      [],
      ['duplicate'],
    ])
  })
})

describe('текстовые строки и пересечения', () => {
  it('не помечает overlap и не превращает подсказки в ошибки', () => {
    const first = textRow({ hints: ['free'] })
    const second = textRow({ line: 2, time: '15:30' })
    const next = recomputeImportRows([first, second], options)

    expect(next.map((row) => row.issues)).toStrictEqual([[], []])
    expect(next.map((row) => row.useEventTime)).toStrictEqual([true, true])
    expect(next[0].hints).toStrictEqual(['free'])
    expect(next[0].startAt).toBe('2026-11-12T10:00:00.000Z')
    expect(next[0].endAt).toBe('2026-11-12T11:00:00.000Z')
    expect(next[1].startAt).toBe('2026-11-12T10:30:00.000Z')
  })
})
