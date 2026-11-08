import { describe, expect, it } from 'vitest'

import type { BarberProfileSalon } from '@/lib/catalog'
import {
  ICS_MAX_OCCURRENCES,
  ICS_MAX_VEVENTS,
  parseIcs,
  type IcsParseOptions,
  type IcsParseResult,
} from '@/lib/ics-parse'

/**
 * Разбор `.ics`: фикстуры — строки, `now` инжектится.
 *
 * Окно по умолчанию: 8 ноября 2026, 15:00 UTC, период 30 дней
 * (начало 14:55Z, конец 8 декабря 15:00Z). Салон — Екатеринбург (+5), поэтому
 * настенное время города переводится в UTC как `−5` часов.
 */

const NOW = Date.parse('2026-11-08T15:00:00Z')

/** `VTIMEZONE` Берлина: нужен, чтобы проверить разрешённый пояс и переходы. */
const BERLIN_TZ = `BEGIN:VTIMEZONE
TZID:Europe/Berlin
BEGIN:STANDARD
DTSTART:19701025T030000
RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
TZOFFSETFROM:+0200
TZOFFSETTO:+0100
TZNAME:CET
END:STANDARD
BEGIN:DAYLIGHT
DTSTART:19700329T020000
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
TZOFFSETFROM:+0100
TZOFFSETTO:+0200
TZNAME:CEST
END:DAYLIGHT
END:VTIMEZONE`

/**
 * Салон-заглушка. Приведение типа нужно один раз: парсеру достаточно
 * `timeZoneId`, а `BarberProfileSalon` описывает весь ответ API.
 */
function salon(
  patch: Partial<BarberProfileSalon> = {}
): BarberProfileSalon {
  return {
    id: 'salon-1',
    salonId: 's1',
    name: 'Ленина',
    address: 'Ленина 1',
    city: 'Екатеринбург',
    country: 'Россия',
    timeZoneId: 'Asia/Yekaterinburg',
    kind: 'Salon',
    isPrimary: true,
    workingSchedules: [],
    ...patch,
  } as BarberProfileSalon
}

function options(
  patch: Partial<IcsParseOptions> = {}
): IcsParseOptions {
  const primary = salon()

  return {
    services: [],
    salons: [primary],
    defaultSalonId: primary.salonId,
    now: NOW,
    periodDays: 30,
    ...patch,
  }
}

/** Календарь из строк: `VTIMEZONE` и события передаются как есть. */
function calendar(...parts: string[]): string {
  return [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//BarberSearch//ICS test//RU',
    ...parts,
    'END:VCALENDAR',
  ].join('\n')
}

function vevent(lines: string): string {
  return ['BEGIN:VEVENT', lines, 'END:VEVENT'].join('\n')
}

/** Календарь с берлинским `VTIMEZONE`. */
function berlin(...parts: string[]): string {
  return calendar(BERLIN_TZ, ...parts)
}

function parse(
  text: string,
  patch: Partial<IcsParseOptions> = {}
): Promise<IcsParseResult> {
  return parseIcs(text, options(patch))
}

function starts(result: IcsParseResult): string[] {
  return result.occurrences.map((occurrence) => occurrence.startAt)
}

describe('одиночные события', () => {
  it('UTC-время, DTEND, тексты и SEQUENCE переносятся как есть', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:single-utc',
            'DTSTART:20261112T100000Z',
            'DTEND:20261112T110000Z',
            'SUMMARY:Стрижка',
            'DESCRIPTION:Тел. +79990000000',
            'LOCATION:Ленина 1',
            'CATEGORIES:HAIRCUT',
            'SEQUENCE:2',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.skipped).toEqual([])
    expect(result.occurrences).toHaveLength(1)
    expect(result.occurrences[0]).toEqual({
      uid: 'single-utc',
      recurrenceId: null,
      summary: 'Стрижка',
      description: 'Тел. +79990000000',
      location: 'Ленина 1',
      categories: 'HAIRCUT',
      startAt: '2026-11-12T10:00:00.000Z',
      endAt: '2026-11-12T11:00:00.000Z',
      durationMinutes: 60,
      sourceTimeZone: 'UTC',
      isRecurring: false,
      status: 'confirmed',
      isFree: false,
      sequence: 2,
    })
  })

  it('TZID без VTIMEZONE строится из компонентов в этом IANA-поясе', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:ekb',
            'DTSTART;TZID=Asia/Yekaterinburg:20261112T100000',
            'DURATION:PT30M',
            'SUMMARY:Стрижка',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0]).toMatchObject({
      startAt: '2026-11-12T05:00:00.000Z',
      endAt: '2026-11-12T05:30:00.000Z',
      durationMinutes: 30,
      sourceTimeZone: 'Asia/Yekaterinburg',
    })
  })

  it('VTIMEZONE Берлина, зима: 10:00 — 09:00Z', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:berlin-winter',
            'DTSTART;TZID=Europe/Berlin:20261112T100000',
            'DTEND;TZID=Europe/Berlin:20261112T104500',
            'SUMMARY:Зима',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0]).toMatchObject({
      startAt: '2026-11-12T09:00:00.000Z',
      endAt: '2026-11-12T09:45:00.000Z',
      durationMinutes: 45,
      sourceTimeZone: 'Europe/Berlin',
    })
  })

  it('VTIMEZONE Берлина, лето: 10:00 — 08:00Z', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:berlin-summer',
            'DTSTART;TZID=Europe/Berlin:20260714T100000',
            'SUMMARY:Лето',
          ].join('\n')
        )
      ),
      { now: Date.parse('2026-07-01T12:00:00Z') }
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0]).toMatchObject({
      startAt: '2026-07-14T08:00:00.000Z',
      sourceTimeZone: 'Europe/Berlin',
    })
  })

  it('floating-время читается по поясу основного салона', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:floating',
            'DTSTART:20261112T100000',
            'SUMMARY:Без пояса',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0]).toMatchObject({
      startAt: '2026-11-12T05:00:00.000Z',
      sourceTimeZone: null,
    })
  })

  it('DTEND без TZID наследует пояс начала', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:end-inherits',
            'DTSTART;TZID=Europe/Berlin:20261112T100000',
            'DTEND:20261112T110000',
            'SUMMARY:Наследование',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0]).toMatchObject({
      startAt: '2026-11-12T09:00:00.000Z',
      endAt: '2026-11-12T10:00:00.000Z',
      durationMinutes: 60,
    })
  })

  it('floating без салонов — unknownTimeZone, а не пояс браузера', async () => {
    const result = await parse(
      calendar(
        vevent(
          ['UID:no-salon', 'DTSTART:20261112T100000', 'SUMMARY:Без пояса'].join(
            '\n'
          )
        )
      ),
      { salons: [], defaultSalonId: '' }
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      { reason: 'unknownTimeZone', summary: 'Без пояса', startAt: null },
    ])
  })

  it('события сортируются по началу, а не по порядку в файле', async () => {
    const result = await parse(
      calendar(
        vevent('UID:late\nDTSTART:20261112T120000Z\nSUMMARY:Позже'),
        vevent('UID:early\nDTSTART:20261112T100000Z\nSUMMARY:Раньше')
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences.map((item) => item.uid)).toEqual([
      'early',
      'late',
    ])
  })
})

describe('пропуски одиночных событий', () => {
  it('VALUE=DATE — allDay', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:all-day',
            'DTSTART;VALUE=DATE:20261112',
            'SUMMARY:Отпуск',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.occurrences).toEqual([])
    expect(result.skipped).toEqual([
      { reason: 'allDay', summary: 'Отпуск', startAt: null },
    ])
  })

  it('нет DTSTART — invalid', async () => {
    const result = await parse(
      calendar(vevent('UID:no-start\nSUMMARY:Без начала'))
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      { reason: 'invalid', summary: 'Без начала', startAt: null },
    ])
  })

  it('пустой UID — noUid', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Без идентификатора',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      {
        reason: 'noUid',
        summary: 'Без идентификатора',
        startAt: '2026-11-12T10:00:00.000Z',
      },
    ])
  })

  it('неизвестный TZID — unknownTimeZone', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:bad-zone',
            'DTSTART;TZID=Bad/Zone:20261112T100000',
            'SUMMARY:Неизвестный пояс',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      { reason: 'unknownTimeZone', summary: 'Неизвестный пояс', startAt: null },
    ])
  })

  it('STATUS:CANCELLED мастера отменяет событие', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:cancelled',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Отменено',
            'STATUS:CANCELLED',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      {
        reason: 'cancelled',
        summary: 'Отменено',
        startAt: '2026-11-12T10:00:00.000Z',
      },
    ])
  })

  it('METHOD:CANCEL у календаря отменяет файл целиком', async () => {
    const result = await parse(
      calendar(
        'METHOD:CANCEL',
        vevent(
          ['UID:method-cancel', 'DTSTART:20261112T100000Z', 'SUMMARY:Отмена'].join(
            '\n'
          )
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.occurrences).toEqual([])
    expect(result.skipped).toHaveLength(1)
    expect(result.skipped[0].reason).toBe('cancelled')
  })
})

describe('длительность, прозрачность и предварительность', () => {
  it('TRANSP:TRANSPARENT не пропускается, а помечается isFree', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:free',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Свободно',
            'TRANSP:TRANSPARENT',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0].isFree).toBe(true)
  })

  it('STATUS:TENTATIVE даёт статус tentative', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:tentative',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Предварительно',
            'STATUS:TENTATIVE',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0].status).toBe('tentative')
  })

  it('нулевая и отрицательная длительность — durationMinutes = null', async () => {
    const zero = await parse(
      calendar(
        vevent(
          [
            'UID:zero',
            'DTSTART:20261112T100000Z',
            'DTEND:20261112T100000Z',
            'SUMMARY:Ноль',
          ].join('\n')
        )
      )
    )
    const negative = await parse(
      calendar(
        vevent(
          [
            'UID:negative',
            'DTSTART:20261112T100000Z',
            'DURATION:-PT30M',
            'SUMMARY:Минус',
          ].join('\n')
        )
      )
    )

    expect(zero.occurrences[0]).toMatchObject({
      endAt: null,
      durationMinutes: null,
    })
    expect(negative.occurrences[0]).toMatchObject({
      endAt: null,
      durationMinutes: null,
    })
  })

  it('13 часов — tooLong, ровно 720 минут разрешено', async () => {
    const tooLong = await parse(
      calendar(
        vevent(
          [
            'UID:too-long',
            'DTSTART:20261112T100000Z',
            'DTEND:20261112T230000Z',
            'SUMMARY:Долго',
          ].join('\n')
        )
      )
    )
    const exactly = await parse(
      calendar(
        vevent(
          [
            'UID:exactly',
            'DTSTART:20261112T100000Z',
            'DTEND:20261112T220000Z',
            'SUMMARY:Ровно 12 часов',
          ].join('\n')
        )
      )
    )

    expect(tooLong.failure).toBe('noEventsInPeriod')
    expect(tooLong.skipped).toEqual([
      {
        reason: 'tooLong',
        summary: 'Долго',
        startAt: '2026-11-12T10:00:00.000Z',
      },
    ])
    expect(exactly.occurrences[0]).toMatchObject({
      durationMinutes: 720,
      endAt: '2026-11-12T22:00:00.000Z',
    })
  })
})

describe('повторяющиеся серии', () => {
  it('WEEKLY;BYDAY;COUNT сохраняет время суток и день недели', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:weekly-count',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=8',
            'SUMMARY:Стрижка',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-17T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-01T09:00:00.000Z',
      '2026-12-08T09:00:00.000Z',
    ])
    expect(result.occurrences[0].isRecurring).toBe(true)
    expect(result.occurrences[0].recurrenceId).toBe(
      '2026-11-10T09:00:00.000Z'
    )
    // 3 ноября — вхождение серии до окна.
    expect(result.skipped).toEqual([
      {
        reason: 'past',
        summary: 'Стрижка',
        startAt: '2026-11-03T09:00:00.000Z',
      },
    ])
  })

  it('INTERVAL > 1 разворачивается от настоящего DTSTART', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:weekly-interval',
            'DTSTART;TZID=Europe/Berlin:20261027T100000',
            'RRULE:FREQ=WEEKLY;INTERVAL=2',
            'SUMMARY:Раз в две недели',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-08T09:00:00.000Z',
    ])
  })

  it('UNTIL ограничивает серию включительно', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:weekly-until',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;UNTIL=20261124T090000Z',
            'SUMMARY:До конца ноября',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-17T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
    ])
  })

  it('EXDATE убирает вхождение', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:weekly-exdate',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;COUNT=5',
            'EXDATE;TZID=Europe/Berlin:20261117T100000',
            'SUMMARY:Без 17-го',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-01T09:00:00.000Z',
    ])
  })

  it('RDATE добавляет вхождение', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:weekly-rdate',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;COUNT=3',
            'RDATE;TZID=Europe/Berlin:20261112T150000',
            'SUMMARY:С доп. датой',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-12T14:00:00.000Z',
      '2026-11-17T09:00:00.000Z',
    ])
  })

  it('RDATE без RRULE: сам DTSTART — тоже вхождение', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:rdate-only',
            'DTSTART;TZID=Europe/Berlin:20261110T100000',
            'RDATE;TZID=Europe/Berlin:20261117T150000',
            'SUMMARY:Только дополнительные даты',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-17T14:00:00.000Z',
    ])
    expect(result.occurrences.every((item) => item.isRecurring)).toBe(true)
  })

  it('override переносит вхождение и отменяет другое', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:override-move',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;COUNT=4',
            'SUMMARY:Мастер',
          ].join('\n')
        ),
        vevent(
          [
            'UID:override-move',
            'RECURRENCE-ID;TZID=Europe/Berlin:20261117T100000',
            'DTSTART;TZID=Europe/Berlin:20261118T140000',
            'DTEND;TZID=Europe/Berlin:20261118T150000',
            'SUMMARY:Перенос',
            'STATUS:TENTATIVE',
            'SEQUENCE:5',
          ].join('\n')
        ),
        vevent(
          [
            'UID:override-move',
            'RECURRENCE-ID;TZID=Europe/Berlin:20261124T100000',
            'DTSTART;TZID=Europe/Berlin:20261124T100000',
            'SUMMARY:Отмена',
            'STATUS:CANCELLED',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-18T13:00:00.000Z',
    ])
    expect(result.occurrences[1]).toMatchObject({
      // Идентичность — исходное вхождение, а не перенесённое время.
      recurrenceId: '2026-11-17T09:00:00.000Z',
      summary: 'Перенос',
      status: 'tentative',
      sequence: 5,
    })
    expect(result.skipped).toEqual([
      {
        reason: 'past',
        summary: 'Мастер',
        startAt: '2026-11-03T09:00:00.000Z',
      },
      {
        reason: 'cancelled',
        summary: 'Отмена',
        startAt: '2026-11-24T09:00:00.000Z',
      },
    ])
  })

  it('отмена override без DTSTART не теряется', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:override-cancel-empty',
            'DTSTART;TZID=Europe/Berlin:20261110T100000',
            'RRULE:FREQ=WEEKLY;COUNT=3',
            'SUMMARY:Мастер',
          ].join('\n')
        ),
        vevent(
          [
            'UID:override-cancel-empty',
            'RECURRENCE-ID;TZID=Europe/Berlin:20261117T100000',
            'STATUS:CANCELLED',
            'SUMMARY:Отмена без времени',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
    ])
    expect(result.skipped).toEqual([
      {
        reason: 'cancelled',
        summary: 'Отмена без времени',
        startAt: null,
      },
    ])
  })

  it('перенос вхождения за окно не обрывает серию', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:override-forward',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;COUNT=8',
            'SUMMARY:Мастер',
          ].join('\n')
        ),
        vevent(
          [
            'UID:override-forward',
            'RECURRENCE-ID;TZID=Europe/Berlin:20261110T100000',
            'DTSTART;TZID=Europe/Berlin:20261220T100000',
            'DTEND;TZID=Europe/Berlin:20261220T110000',
            'SUMMARY:За окном',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    // 10 ноября перенесено за окно; следующие вхождения серии не потеряны.
    expect(starts(result)).toEqual([
      '2026-11-17T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-01T09:00:00.000Z',
      '2026-12-08T09:00:00.000Z',
    ])
  })

  it('override, перенесённый из прошлого в окно, не теряется', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:moved-from-past',
            'DTSTART;TZID=Europe/Berlin:20150106T100000',
            'RRULE:FREQ=WEEKLY',
            'SUMMARY:Старая серия',
          ].join('\n')
        ),
        vevent(
          [
            'UID:moved-from-past',
            'RECURRENCE-ID;TZID=Europe/Berlin:20261103T100000',
            'DTSTART;TZID=Europe/Berlin:20261112T140000',
            'DTEND;TZID=Europe/Berlin:20261112T150000',
            'SUMMARY:Перенос из прошлого',
            'SEQUENCE:2',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-12T13:00:00.000Z',
      '2026-11-17T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-01T09:00:00.000Z',
      '2026-12-08T09:00:00.000Z',
    ])
    expect(result.occurrences[1]).toMatchObject({
      recurrenceId: '2026-11-03T09:00:00.000Z',
      summary: 'Перенос из прошлого',
    })
  })

  it('RECURRENCE-ID;RANGE=THISANDFUTURE не роняет разбор', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:range',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;COUNT=6',
            'SUMMARY:Мастер',
          ].join('\n')
        ),
        vevent(
          [
            'UID:range',
            'RECURRENCE-ID;RANGE=THISANDFUTURE;TZID=Europe/Berlin:20261117T100000',
            'DTSTART;TZID=Europe/Berlin:20261117T120000',
            'DTEND;TZID=Europe/Berlin:20261117T130000',
            'SUMMARY:Диапазон',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-17T11:00:00.000Z',
      '2026-11-24T11:00:00.000Z',
      '2026-12-01T11:00:00.000Z',
      '2026-12-08T11:00:00.000Z',
    ])
    expect(result.occurrences[1].summary).toBe('Диапазон')
  })
})

describe('месячные и годовые правила', () => {
  it('MONTHLY без BY-частей сохраняет день месяца DTSTART', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:monthly-plain',
            'DTSTART;TZID=Europe/Berlin:20150130T100000',
            'RRULE:FREQ=MONTHLY',
            'SUMMARY:Каждое 30-е',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual(['2026-11-30T09:00:00.000Z'])
  })

  it('MONTHLY 31-го не превращается в 1-е число следующего месяца', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:monthly-31',
            'DTSTART;TZID=Europe/Berlin:20150131T100000',
            'RRULE:FREQ=MONTHLY',
            'SUMMARY:Каждое 31-е',
          ].join('\n')
        )
      ),
      { now: Date.parse('2026-12-15T00:00:00Z') }
    )

    expect(result.failure).toBeNull()
    // В ноябре 31-го нет, поэтому ближайшее вхождение — 31 декабря.
    expect(starts(result)).toEqual(['2026-12-31T09:00:00.000Z'])
  })

  it('YEARLY;BYMONTH сохраняет день DTSTART, а не день окна', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:yearly-bymonth',
            'DTSTART;TZID=Europe/Berlin:20150315T100000',
            'RRULE:FREQ=YEARLY;BYMONTH=3',
            'SUMMARY:Каждый март',
          ].join('\n')
        )
      ),
      { now: Date.parse('2026-02-20T00:00:00Z') }
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual(['2026-03-15T09:00:00.000Z'])
  })

  it('YEARLY;BYMONTHDAY без BYMONTH наследует месяц DTSTART', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:yearly-bymonthday',
            'DTSTART;TZID=Europe/Berlin:20150315T100000',
            'RRULE:FREQ=YEARLY;BYMONTHDAY=15',
            'SUMMARY:15-е числа марта',
          ].join('\n')
        )
      ),
      { now: Date.parse('2026-02-20T00:00:00Z') }
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual(['2026-03-15T09:00:00.000Z'])
  })
})

describe('окно и ограничения итераций', () => {
  it('время суток DTSTART сохраняется при сдвиге недельной серии', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:time-of-day',
            'DTSTART;TZID=Europe/Berlin:20260106T100000',
            'RRULE:FREQ=WEEKLY;BYDAY=TU',
            'SUMMARY:Каждый вторник',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences.length).toBeGreaterThan(0)
    expect(
      result.occurrences.every((occurrence) =>
        occurrence.startAt.endsWith('T09:00:00.000Z')
      )
    ).toBe(true)
    expect(result.occurrences[0].startAt).toBe('2026-11-10T09:00:00.000Z')
  })

  it('ежедневная серия 2015 года не упирается в лимит итераций', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:daily-2015',
            'DTSTART;TZID=Europe/Berlin:20150101T100000',
            'RRULE:FREQ=DAILY',
            'SUMMARY:Каждый день',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences).toHaveLength(30)
    expect(result.occurrences[0].startAt).toBe('2026-11-09T09:00:00.000Z')
    expect(result.occurrences.at(-1)?.startAt).toBe(
      '2026-12-08T09:00:00.000Z'
    )
    expect(result.skipped).toEqual([
      {
        reason: 'past',
        summary: 'Каждый день',
        startAt: '2026-11-08T09:00:00.000Z',
      },
    ])
  })

  it('недельная серия 2015 года разворачивается от окна', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:weekly-2015',
            'DTSTART;TZID=Europe/Berlin:20150106T100000',
            'RRULE:FREQ=WEEKLY',
            'SUMMARY:Каждый вторник',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-17T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-01T09:00:00.000Z',
      '2026-12-08T09:00:00.000Z',
    ])
  })

  it('COUNT=8 серии 2015 года не воскрешает будущие вхождения', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:count-2015',
            'DTSTART;TZID=Europe/Berlin:20150106T100000',
            'RRULE:FREQ=WEEKLY;COUNT=8',
            'SUMMARY:Восемь раз в 2015',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.occurrences).toEqual([])
    expect(result.skipped.every((item) => item.reason === 'past')).toBe(true)
  })

  it('INTERVAL-серия 2015 года упирается в лимит итераций', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:interval-cap',
            'DTSTART;TZID=Europe/Berlin:20150101T100000',
            'RRULE:FREQ=DAILY;INTERVAL=2',
            'SUMMARY:Раз в два дня',
          ].join('\n')
        )
      )
    )

    // Фаза такой серии зависит от DTSTART, поэтому итератор идёт с 2015 года
    // и до окна 2026-го не доходит: результат неполный, и это ошибка.
    expect(result.failure).toBe('tooManyOccurrences')
    expect(result.occurrences).toEqual([])
  })

  it('ежедневная UTC-серия 2015 года разворачивается от окна', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:daily-utc-2015',
            'DTSTART:20150101T100000Z',
            'RRULE:FREQ=DAILY',
            'SUMMARY:Каждый день по UTC',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences).toHaveLength(30)
    expect(result.occurrences[0].startAt).toBe('2026-11-09T10:00:00.000Z')
    expect(result.occurrences[0].sourceTimeZone).toBe('UTC')
  })

  it('бесконечная floating-серия считается по поясу салона', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:weekly-floating-2015',
            'DTSTART:20150101T100000',
            'RRULE:FREQ=WEEKLY',
            'SUMMARY:Каждый четверг',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-12T05:00:00.000Z',
      '2026-11-19T05:00:00.000Z',
      '2026-11-26T05:00:00.000Z',
      '2026-12-03T05:00:00.000Z',
    ])
    expect(result.occurrences[0].sourceTimeZone).toBeNull()
  })

  it('серия с IANA TZID без VTIMEZONE разворачивается от окна', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:weekly-unregistered-2015',
            'DTSTART;TZID=Europe/Berlin:20150106T100000',
            'RRULE:FREQ=WEEKLY',
            'SUMMARY:Вторники без VTIMEZONE',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(starts(result)).toEqual([
      '2026-11-10T09:00:00.000Z',
      '2026-11-17T09:00:00.000Z',
      '2026-11-24T09:00:00.000Z',
      '2026-12-01T09:00:00.000Z',
      '2026-12-08T09:00:00.000Z',
    ])
    expect(result.occurrences[0].sourceTimeZone).toBe('Europe/Berlin')
  })

  it('ровно начало окна включается, секунда раньше — past', async () => {
    const atStart = await parse(
      calendar(
        vevent(
          [
            'UID:at-start',
            'DTSTART:20261108T145500Z',
            'SUMMARY:На границе',
          ].join('\n')
        )
      )
    )
    const before = await parse(
      calendar(
        vevent(
          [
            'UID:before-start',
            'DTSTART:20261108T145459Z',
            'SUMMARY:До границы',
          ].join('\n')
        )
      )
    )

    expect(atStart.failure).toBeNull()
    expect(atStart.occurrences[0].startAt).toBe('2026-11-08T14:55:00.000Z')
    expect(before.failure).toBe('noEventsInPeriod')
    expect(before.skipped).toEqual([
      {
        reason: 'past',
        summary: 'До границы',
        startAt: '2026-11-08T14:54:59.000Z',
      },
    ])
  })

  it('ровно конец окна — outsidePeriod', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:at-end',
            'DTSTART:20261208T150000Z',
            'SUMMARY:Конец окна',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      {
        reason: 'outsidePeriod',
        summary: 'Конец окна',
        startAt: '2026-12-08T15:00:00.000Z',
      },
    ])
  })
})

describe('идентификаторы и несколько мастеров', () => {
  it('несколько мастеров одного UID: побеждает наибольший SEQUENCE', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:duplicate',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Старая',
            'SEQUENCE:1',
          ].join('\n')
        ),
        vevent(
          [
            'UID:duplicate',
            'DTSTART:20261112T110000Z',
            'SUMMARY:Первая с тем же SEQUENCE',
            'SEQUENCE:3',
          ].join('\n')
        ),
        vevent(
          [
            'UID:duplicate',
            'DTSTART:20261112T120000Z',
            'SUMMARY:Новая',
            'SEQUENCE:3',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences).toHaveLength(1)
    expect(result.occurrences[0]).toMatchObject({
      summary: 'Первая с тем же SEQUENCE',
      sequence: 3,
    })
    // Равный SEQUENCE — первый в файле.
    expect(result.occurrences[0].startAt).toBe('2026-11-12T11:00:00.000Z')
  })

  it('отмена мастера отменяет всю серию, включая override', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:cancelled-series',
            'DTSTART;TZID=Europe/Berlin:20261103T100000',
            'RRULE:FREQ=WEEKLY;COUNT=4',
            'STATUS:CANCELLED',
            'SUMMARY:Серия отменена',
          ].join('\n')
        ),
        vevent(
          [
            'UID:cancelled-series',
            'RECURRENCE-ID;TZID=Europe/Berlin:20261110T100000',
            'DTSTART;TZID=Europe/Berlin:20261110T100000',
            'SUMMARY:Отдельное',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.occurrences).toEqual([])
    expect(result.skipped).toHaveLength(1)
    expect(result.skipped[0].reason).toBe('cancelled')
    expect(result.skipped[0].summary).toBe('Серия отменена')
  })
})

describe('переходы на летнее время', () => {
  it('дыра в поясе без VTIMEZONE — invalid', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:dst-gap',
            'DTSTART;TZID=Europe/Berlin:20260329T023000',
            'SUMMARY:Несуществующее время',
          ].join('\n')
        )
      ),
      { now: Date.parse('2026-03-29T00:00:00Z') }
    )

    expect(result.failure).toBe('noEventsInPeriod')
    expect(result.skipped).toEqual([
      {
        reason: 'invalid',
        summary: 'Несуществующее время',
        startAt: null,
      },
    ])
  })

  it('повторяющийся час стабилен между разборами', async () => {
    const fixture = berlin(
      vevent(
        [
          'UID:dst-ambiguous',
          'DTSTART;TZID=Europe/Berlin:20261025T023000',
          'SUMMARY:Повторяющийся час',
        ].join('\n')
      )
    )
    const first = await parse(fixture, {
      now: Date.parse('2026-10-25T00:00:00Z'),
    })
    const second = await parse(fixture, {
      now: Date.parse('2026-10-25T00:00:00Z'),
    })

    expect(first.failure).toBeNull()
    expect(first.occurrences[0].startAt).toBe('2026-10-25T01:30:00.000Z')
    expect(second.occurrences[0].startAt).toBe(
      first.occurrences[0].startAt
    )
  })
})

describe('экранирование и перенос строк', () => {
  it('folded-строка и escapes \\, \\; \\n раскрываются', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:escaped',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Иван\\, Петров\\; стрижка\\nвторая строка',
            'DESCRIPTION:Описание\\, с точкой с запятой\\; и переводом\\nстроки',
            'LOCATION:Ленина\\, 1',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0].description).toBe(
      'Описание, с точкой с запятой; и переводом\nстроки'
    )
    expect(result.occurrences[0].location).toBe('Ленина, 1')
    expect(result.occurrences[0].summary).toBe(
      'Иван, Петров; стрижка\nвторая строка'
    )
  })

  it('folded-строка склеивается без потери текста', async () => {
    const result = await parse(
      calendar(
        vevent(
          [
            'UID:folded',
            'DTSTART:20261112T100000Z',
            'SUMMARY:Очень длинное название ',
            ' которое перенесено',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBeNull()
    expect(result.occurrences[0].summary).toBe(
      'Очень длинное название которое перенесено'
    )
  })
})

describe('лимиты и ошибки файла', () => {
  it('5001 VEVENT — tooManyEvents', async () => {
    const events: string[] = []

    for (let index = 0; index <= ICS_MAX_VEVENTS; index += 1) {
      events.push(
        vevent(
          [
            `UID:bulk-${index}`,
            'DTSTART:20261112T100000Z',
            `SUMMARY:Событие ${index}`,
          ].join('\n')
        )
      )
    }

    const result = await parse(calendar(...events))

    expect(result).toEqual({
      occurrences: [],
      skipped: [],
      failure: 'tooManyEvents',
    })
  })

  it('больше 500 вхождений в окне — tooManyOccurrences без вхождений', async () => {
    const result = await parse(
      berlin(
        vevent(
          [
            'UID:too-many',
            'DTSTART;TZID=Europe/Berlin:20261101T100000',
            'RRULE:FREQ=HOURLY',
            'SUMMARY:Каждый час',
          ].join('\n')
        )
      )
    )

    expect(result.failure).toBe('tooManyOccurrences')
    expect(result.occurrences).toEqual([])
    expect(ICS_MAX_OCCURRENCES).toBe(500)
  })

  it('битый файл — invalid, без исключения', async () => {
    const result = await parse('BEGIN:VCALENDAR\nBEGIN:VEVENT')

    expect(result).toEqual({
      occurrences: [],
      skipped: [],
      failure: 'invalid',
    })
  })

  it('календарь без VEVENT — empty', async () => {
    const result = await parse(calendar(BERLIN_TZ))

    expect(result).toEqual({
      occurrences: [],
      skipped: [],
      failure: 'empty',
    })
  })

  it('пустой текст — invalid, без исключения', async () => {
    const result = await parse('')

    expect(result.failure).toBe('invalid')
    expect(result.occurrences).toEqual([])
  })
})
