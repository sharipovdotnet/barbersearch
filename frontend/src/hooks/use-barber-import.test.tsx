import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  useBarberImport,
  type BarberImportController,
  type ImportRow,
} from '@/hooks/use-barber-import'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Хук переноса: разбор текста и календаря, проверка, последовательная отправка.
 *
 * Сеть — только MSW. Текстовые даты относительные («завтра»), а календарь живёт
 * в фиксированном окне: системные часы подменяются на 8 ноября 2026, 15:00 UTC,
 * поэтому события 10–13 ноября всегда в будущем и внутри 30-дневного периода.
 */

const OPTIONS_PATH = '/api/barber/booking-options'
const BOOKINGS_PATH = '/api/barber/bookings'
const SLOTS_PATH = '/api/barber/slots'
const DAY_MS = 24 * 60 * 60 * 1000

const CALENDAR_NOW = new Date('2026-11-08T15:00:00Z')

const services = [
  {
    barberServiceId: 'svc-haircut',
    hairServiceId: 'hair-haircut',
    name: 'Стрижка',
    category: 'Haircut',
    defaultDurationMinutes: 60,
    price: 2000,
    durationMinutes: 60,
    isActive: true,
  },
  {
    barberServiceId: 'svc-beard',
    hairServiceId: 'hair-beard',
    name: 'Борода',
    category: 'Beard',
    defaultDurationMinutes: 30,
    price: 1200,
    durationMinutes: 30,
    isActive: true,
  },
]

const salons = [
  {
    id: 'profile-salon-1',
    salonId: 'salon-1',
    name: 'Салон на Ленина',
    address: 'ул. Ленина, 1',
    city: 'Екатеринбург',
    country: 'Russia',
    timeZoneId: 'Asia/Yekaterinburg',
    kind: 'Salon',
    isPrimary: true,
    workingSchedules: [],
  },
]

type BookingCall = {
  key: string
  firstName: string
  startAt: string
  endAt: string
}

/** Один POST — одна строка: счётчик и тела фиксируются в порядке отправки. */
function bookingHandler(
  calls: BookingCall[],
  respond: (call: BookingCall, index: number) => Response
) {
  return http.post(BOOKINGS_PATH, async ({ request }) => {
    const body = (await request.json()) as {
      guestFirstName: string
      startAt: string
      endAt: string
      idempotencyKey: string
    }

    const call: BookingCall = {
      key: body.idempotencyKey,
      firstName: body.guestFirstName,
      startAt: body.startAt,
      endAt: body.endAt,
    }
    calls.push(call)

    return respond(call, calls.length - 1)
  })
}

function created(id: string) {
  return HttpResponse.json({ id }, { status: 201 })
}

function optionsHandler() {
  return http.get(OPTIONS_PATH, () =>
    HttpResponse.json({ services, salons })
  )
}

beforeEach(() => {
  server.use(optionsHandler())
})

afterEach(() => {
  vi.useRealTimers()
})

async function renderImport() {
  const hook = renderHook(() => useBarberImport('profile-1'))

  await waitFor(() => {
    expect(hook.result.current.optionsStatus).toBe('ready')
  })

  return hook
}

function parseText(
  hook: { result: { current: BarberImportController } },
  text: string
) {
  // Текстовый путь начинается с явного переключения источника: по умолчанию
  // открыт «Файл календаря».
  act(() => {
    hook.result.current.setSource('text')
  })
  act(() => {
    hook.result.current.setText(text)
  })
  act(() => {
    hook.result.current.runParse()
  })
}

/** Фикстура календаря: визит, all-day, прошедшее событие и серия с переносом. */
function calendarText(): string {
  return [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//BarberSearch//Import test//EN',
    'BEGIN:VEVENT',
    'UID:single-1',
    'SUMMARY:Стрижка — Иван Петров',
    'DESCRIPTION:тел. +79990001122',
    'DTSTART:20261112T100000Z',
    'DTEND:20261112T104500Z',
    'END:VEVENT',
    'BEGIN:VEVENT',
    'UID:all-day-1',
    'SUMMARY:Отпуск',
    'DTSTART;VALUE=DATE:20261110',
    'DTEND;VALUE=DATE:20261111',
    'END:VEVENT',
    'BEGIN:VEVENT',
    'UID:past-1',
    'SUMMARY:Прошлый визит',
    'DTSTART:20261101T100000Z',
    'DTEND:20261101T110000Z',
    'END:VEVENT',
    'BEGIN:VEVENT',
    'UID:series-1',
    'SUMMARY:Борода — Пётр Сидоров',
    'DESCRIPTION:тел. +79990002233',
    'DTSTART:20261110T120000Z',
    'DTEND:20261110T123000Z',
    'RRULE:FREQ=DAILY;COUNT=2',
    'END:VEVENT',
    'BEGIN:VEVENT',
    'UID:series-1',
    'RECURRENCE-ID:20261111T120000Z',
    'SUMMARY:Борода — Пётр Сидоров',
    'DESCRIPTION:тел. +79990002233',
    'DTSTART:20261113T120000Z',
    'DTEND:20261113T130000Z',
    'END:VEVENT',
    'END:VCALENDAR',
  ].join('\r\n')
}

function calendarFile(text = calendarText()): File {
  return new File([text], 'cal.ics', { type: 'text/calendar' })
}

/**
 * Календарь на `count` событий: по два в день, без пересечений.
 *
 * Нужен только для проверки лимита выбора: 51 событие не влезает в одну пачку.
 */
function bulkCalendar(count: number): string {
  const start = Date.parse('2026-11-10T00:00:00Z')
  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//BarberSearch//Bulk test//EN',
  ]

  for (let index = 0; index < count; index += 1) {
    const date = new Date(start + Math.floor(index / 2) * DAY_MS)
      .toISOString()
      .slice(0, 10)
      .replace(/-/g, '')
    const hour = index % 2 === 0 ? '10' : '12'

    lines.push(
      'BEGIN:VEVENT',
      `UID:bulk-${index}`,
      `SUMMARY:Стрижка — Клиент ${index}`,
      `DESCRIPTION:тел. +7999${String(1000000 + index)}`,
      `DTSTART:${date}T${hour}0000Z`,
      `DTEND:${date}T${hour}3000Z`,
      'END:VEVENT'
    )
  }

  lines.push('END:VCALENDAR')

  return lines.join('\r\n')
}

function calendarClock(): void {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(CALENDAR_NOW)
}

function findRow(
  hook: { result: { current: BarberImportController } },
  firstName: string
): ImportRow {
  const row = hook.result.current.rows.find(
    (item) => item.draft.firstName === firstName
  )

  if (row === undefined) {
    throw new Error(`Нет строки с именем «${firstName}».`)
  }

  return row
}

async function parseCalendar(
  hook: { result: { current: BarberImportController } },
  text = calendarText()
): Promise<void> {
  act(() => {
    hook.result.current.selectFile(calendarFile(text))
  })
  await act(async () => {
    await hook.result.current.parseFile()
  })
}

describe('useBarberImport: разбор', () => {
  it('сводка считает готовые строки и ошибки', async () => {
    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n12:00 Катя\n13:00 Пётр борода +79990003344'
    )

    expect(hook.result.current.stage).toBe('preview')
    expect(hook.result.current.summary.total).toBe(3)
    expect(hook.result.current.summary.selected).toBe(3)
    expect(hook.result.current.summary.ready).toBe(2)
    expect(hook.result.current.summary.missingService).toBe(1)
    expect(hook.result.current.rows[1].draft.issues).toStrictEqual([
      'noPhone',
      'noService',
    ])
  })

  it('дубль внутри пачки блокирует вторую строку', async () => {
    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n10:00 Пётр стрижка +79990003344'
    )

    expect(hook.result.current.rows[0].draft.issues).toStrictEqual([])
    expect(hook.result.current.rows[1].draft.issues).toStrictEqual(['duplicate'])
    expect(hook.result.current.summary.ready).toBe(1)
  })

  it('лимит строк — ошибка формы, разбора нет', async () => {
    const hook = await renderImport()
    const lines = ['завтра']

    for (let index = 0; index < 50; index += 1) {
      lines.push(`10:00 Клиент${index} стрижка +79990001122`)
    }

    parseText(hook, lines.join('\n'))

    expect(hook.result.current.stage).toBe('form')
    expect(hook.result.current.failure).toBe('Слишком много строк: максимум 50.')
    expect(hook.result.current.rows).toStrictEqual([])
  })
})

describe('useBarberImport: отправка', () => {
  it('отправляет строки последовательно, по одной', async () => {
    const calls: BookingCall[] = []
    let inFlight = 0
    let maxInFlight = 0

    server.use(
      http.post(BOOKINGS_PATH, async ({ request }) => {
        const body = (await request.json()) as {
          guestFirstName: string
          startAt: string
          endAt: string
          idempotencyKey: string
        }

        inFlight += 1
        maxInFlight = Math.max(maxInFlight, inFlight)
        await new Promise((resolve) => setTimeout(resolve, 5))
        inFlight -= 1

        calls.push({
          key: body.idempotencyKey,
          firstName: body.guestFirstName,
          startAt: body.startAt,
          endAt: body.endAt,
        })

        return created(`booking-${calls.length}`)
      })
    )

    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n11:00 Пётр борода +79990003344'
    )

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(calls.map((call) => call.firstName)).toStrictEqual(['Иван', 'Пётр'])
    // Параллельный запуск увидел бы два запроса одновременно.
    expect(maxInFlight).toBe(1)
    expect(hook.result.current.stage).toBe('result')
    expect(hook.result.current.summary.created).toBe(2)
    // Для текста выбранными считаются все строки, и после пачки «ещё» нечего.
    expect(hook.result.current.summary.selected).toBe(2)
    expect(hook.result.current.canContinue).toBe(false)
  })

  it('400 tooFar и period.tooLong получают свои тексты, а не «Обновите свободное время»', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call, index) =>
        index === 0
          ? HttpResponse.json(
              problemDetails(400, {
                errorCodes: ['booking.create.startAt.tooFar'],
              }),
              { status: 400 }
            )
          : HttpResponse.json(
              problemDetails(400, {
                errorCodes: ['booking.create.period.tooLong'],
              }),
              { status: 400 }
            )
      )
    )

    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n11:00 Пётр борода +79990003344'
    )

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.rows[0].failure).toBe(
      'Слишком далеко: запись можно создать не более чем на 90 дней вперёд.'
    )
    expect(hook.result.current.rows[1].failure).toBe('Слишком длинное событие.')

    for (const row of hook.result.current.rows) {
      expect(row.failure).not.toContain('Обновите свободное время')
    }
  })

  it('текстовый импорт с упавшей строкой не предлагает «Перенести ещё»', async () => {
    server.use(
      bookingHandler([], () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['booking.create.startAt.inPast'],
          }),
          { status: 400 }
        )
      )
    )

    const hook = await renderImport()

    parseText(hook, 'завтра\n10:00 Иван стрижка +79990001122')

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.rows[0].status).toBe('failed')
    expect(hook.result.current.canContinue).toBe(false)
  })

  it('повтор неудачной строки идёт с тем же ключом идемпотентности', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call, index) =>
        index === 0
          ? HttpResponse.json(
              problemDetails(409, { errorCodes: ['booking.add.overlap'] }),
              { status: 409 }
            )
          : created('booking-retry')
      ),
      http.get(SLOTS_PATH, () =>
        HttpResponse.json({
          barberProfileId: 'profile-1',
          salonId: 'salon-1',
          barberServiceId: 'svc-haircut',
          hairServiceId: 'hair-haircut',
          date: '2026-11-12',
          timeZoneId: 'Asia/Yekaterinburg',
          dayStart: '2026-11-12T05:00:00.000Z',
          dayEnd: '2026-11-12T15:00:00.000Z',
          durationMinutes: 60,
          isWorkingDay: true,
          slots: [],
        })
      )
    )

    const hook = await renderImport()

    parseText(hook, 'завтра\n10:00 Иван стрижка +79990001122')

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.rows[0].status).toBe('failed')
    expect(hook.result.current.rows[0].failure).toBe('Время занято.')

    await act(async () => {
      await hook.result.current.retryFailed()
    })

    expect(calls).toHaveLength(2)
    expect(calls[0].key).toBe(calls[1].key)
    expect(hook.result.current.rows[0].status).toBe('created')
    expect(hook.result.current.summary.created).toBe(1)
  })

  it('409 просит ближайшие слоты и подставляет выбранное время', async () => {
    const calls: BookingCall[] = []

    const slot = {
      startAt: '2099-05-05T07:00:00.000Z',
      endAt: '2099-05-05T08:00:00.000Z',
    }

    server.use(
      bookingHandler(calls, (_call, index) =>
        index === 0
          ? HttpResponse.json(
              problemDetails(409, { errorCodes: ['booking.add.overlap'] }),
              { status: 409 }
            )
          : created('booking-slot')
      ),
      http.get(SLOTS_PATH, () => HttpResponse.json({
        barberProfileId: 'profile-1',
        salonId: 'salon-1',
        barberServiceId: 'svc-haircut',
        hairServiceId: 'hair-haircut',
        date: '2099-05-05',
        timeZoneId: 'Asia/Yekaterinburg',
        dayStart: '2099-05-05T05:00:00.000Z',
        dayEnd: '2099-05-05T15:00:00.000Z',
        durationMinutes: 60,
        isWorkingDay: true,
        slots: [slot],
      }))
    )

    const hook = await renderImport()

    parseText(hook, 'завтра\n10:00 Иван стрижка +79990001122')

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.rows[0].overlap).toBe(true)
    expect(hook.result.current.rows[0].slots).toStrictEqual([slot])

    await act(async () => {
      await hook.result.current.applySlot(
        hook.result.current.rows[0].draft.line,
        slot
      )
    })

    expect(calls).toHaveLength(2)
    // Время подставлено из слота: 12:00 по Екатеринбургу.
    expect(calls[1].startAt).toBe('2099-05-05T07:00:00.000Z')
    expect(hook.result.current.rows[0].status).toBe('created')
  })

  it('429 останавливает пачку, «Продолжить» досылает остаток', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call, index) =>
        index === 1
          ? HttpResponse.json(
              problemDetails(429, { errorCodes: ['rate.limit'] }),
              { status: 429 }
            )
          : created(`booking-${index}`)
      )
    )

    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n11:00 Пётр борода +79990003344'
    )

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(calls).toHaveLength(2)
    expect(hook.result.current.pause?.kind).toBe('rateLimit')
    expect(hook.result.current.pause?.message).toBe(
      'Слишком много запросов. Продолжим через минуту.'
    )
    expect(hook.result.current.stage).toBe('preview')

    await act(async () => {
      await hook.result.current.resume()
    })

    expect(calls).toHaveLength(3)
    expect(hook.result.current.summary.created).toBe(2)
    expect(hook.result.current.stage).toBe('result')
  })

  it('401 поднимает вход и не трогает остальные строки', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, () =>
        HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.required'] }),
          { status: 401 }
        )
      )
    )

    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n11:00 Пётр борода +79990003344'
    )

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(calls).toHaveLength(1)
    expect(hook.result.current.pause?.kind).toBe('signIn')
    expect(hook.result.current.rows[0].status).toBe('ready')
  })

  it('сеть останавливает пачку с предложением продолжить', async () => {
    server.use(
      http.post(BOOKINGS_PATH, () => {
        throw new TypeError('failed to fetch')
      })
    )

    const hook = await renderImport()

    parseText(hook, 'завтра\n10:00 Иван стрижка +79990001122')

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.pause).toStrictEqual({
      kind: 'network',
      message: 'Связь пропала. Продолжить с этой строки.',
    })
  })

  it('размонтирование останавливает пачку без отправки следующей строки', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call) => created('booking-1'))
    )

    const hook = await renderImport()

    parseText(
      hook,
      'завтра\n10:00 Иван стрижка +79990001122\n11:00 Пётр борода +79990003344'
    )

    let promise: Promise<void> = Promise.resolve()

    act(() => {
      promise = hook.result.current.createRows()
    })

    hook.unmount()
    await act(async () => {
      await promise
    })

    // Первая строка уже ушла — её не отменить; вторая не отправляется.
    expect(calls).toHaveLength(1)
  })
})

describe('useBarberImport: файл календаря', () => {
  beforeEach(() => {
    calendarClock()
  })

  it('selectFile сохраняет файл, но не разбирает его', async () => {
    const hook = await renderImport()
    const read = vi.spyOn(File.prototype, 'arrayBuffer')
    const first = calendarFile()

    act(() => {
      hook.result.current.selectFile(first)
    })

    expect(hook.result.current.file).toStrictEqual({
      name: 'cal.ics',
      size: first.size,
    })
    expect(hook.result.current.stage).toBe('form')
    expect(hook.result.current.isParsing).toBe(false)
    expect(hook.result.current.rows).toStrictEqual([])
    expect(read).not.toHaveBeenCalled()

    // Смена файла до первого разбора тоже ничего не парсит.
    const second = new File(
      [bulkCalendar(1)],
      'other.ics',
      { type: 'text/calendar' }
    )

    act(() => {
      hook.result.current.selectFile(second)
    })

    expect(hook.result.current.file).toStrictEqual({
      name: 'other.ics',
      size: second.size,
    })
    expect(read).not.toHaveBeenCalled()

    await act(async () => {
      await hook.result.current.parseFile()
    })

    // Разобран именно последний выбранный файл, и прочитан он один раз.
    expect(hook.result.current.file?.name).toBe('other.ics')
    expect(hook.result.current.summary.total).toBe(1)
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('parseFile разбирает календарь и считает пропущенные события', async () => {
    const hook = await renderImport()
    const file = calendarFile()

    act(() => {
      hook.result.current.selectFile(file)
    })
    await act(async () => {
      await hook.result.current.parseFile()
    })

    expect(hook.result.current.stage).toBe('preview')
    expect(hook.result.current.isParsing).toBe(false)
    expect(hook.result.current.file).toStrictEqual({
      name: 'cal.ics',
      size: file.size,
    })
    expect(hook.result.current.rows).toHaveLength(3)
    expect(hook.result.current.summary).toStrictEqual({
      total: 3,
      selected: 3,
      ready: 3,
      created: 0,
      failed: 0,
      missingService: 0,
      missingPhone: 0,
      past: 0,
      skipped: 2,
    })
    expect(
      hook.result.current.skipped.map((event) => event.reason).sort()
    ).toStrictEqual(['allDay', 'past'])
    expect(
      hook.result.current.rows.every((row) =>
        row.idempotencyKey.startsWith('ics:')
      )
    ).toBe(true)
  })

  it('календарь с не созданной строкой предлагает «Перенести ещё»', async () => {
    const hook = await renderImport()

    await parseCalendar(hook)

    expect(
      hook.result.current.rows.some((row) => row.status !== 'created')
    ).toBe(true)
    expect(hook.result.current.canContinue).toBe(true)
  })

  it('до createRows в сеть не уходит ни одной брони', async () => {
    const calls: BookingCall[] = []

    server.use(bookingHandler(calls, () => created('booking-1')))

    const hook = await renderImport()

    await parseCalendar(hook)

    expect(hook.result.current.stage).toBe('preview')
    expect(calls).toHaveLength(0)
  })

  it('снятая галочка не отправляется', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call, index) => created(`booking-${index}`))
    )

    const hook = await renderImport()

    await parseCalendar(hook)

    const removed = findRow(hook, 'Иван')

    act(() => {
      hook.result.current.toggleRow(removed.draft.line)
    })

    expect(hook.result.current.summary.selected).toBe(2)

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(calls).toHaveLength(2)
    expect(calls.some((call) => call.firstName === 'Иван')).toBe(false)
  })

  it('51-я строка не выбирается, выбор остаётся 50', async () => {
    const hook = await renderImport()

    await parseCalendar(hook, bulkCalendar(51))

    expect(hook.result.current.rows).toHaveLength(51)
    expect(hook.result.current.summary.selected).toBe(50)

    const last = hook.result.current.rows[50]

    expect(last.included).toBe(false)

    act(() => {
      hook.result.current.toggleRow(last.draft.line)
    })

    expect(hook.result.current.selectionNotice).toBe(
      'За один раз — не больше 50 записей.'
    )
    expect(hook.result.current.summary.selected).toBe(50)
    expect(hook.result.current.rows[50].included).toBe(false)

    const first = hook.result.current.rows[0]

    act(() => {
      hook.result.current.toggleRow(first.draft.line)
    })

    // Успешное снятие галочки убирает предупреждение.
    expect(hook.result.current.selectionNotice).toBeNull()
    expect(hook.result.current.summary.selected).toBe(49)

    act(() => {
      hook.result.current.toggleRow(last.draft.line)
    })

    expect(hook.result.current.summary.selected).toBe(50)
    expect(hook.result.current.rows[50].included).toBe(true)

    act(() => {
      hook.result.current.setAllIncluded(false)
    })

    expect(hook.result.current.summary.selected).toBe(0)
    expect(hook.result.current.selectionNotice).toBeNull()

    act(() => {
      hook.result.current.setAllIncluded(true)
    })

    // «Выбрать все» тоже упирается в предел: 51-я строка не выбирается,
    // и предупреждение объясняет, почему.
    expect(hook.result.current.summary.selected).toBe(50)
    expect(hook.result.current.selectionNotice).toBe(
      'За один раз — не больше 50 записей.'
    )

    act(() => {
      hook.result.current.toggleRow(first.draft.line)
    })

    expect(hook.result.current.selectionNotice).toBeNull()
  })

  it('updateRow правит телефон строки без ошибок и сохраняет остальное', async () => {
    const hook = await renderImport()

    await parseCalendar(hook)

    const before = findRow(hook, 'Иван')
    const line = before.draft.line

    act(() => {
      hook.result.current.updateRow(line, { phone: '+7 (999) 555-66-77' })
    })

    const next = findRow(hook, 'Иван')

    expect(next.draft.phone).toBe('+79995556677')
    expect(next.draft.firstName).toBe(before.draft.firstName)
    expect(next.draft.lastName).toBe(before.draft.lastName)
    expect(next.draft.startAt).toBe(before.draft.startAt)
    expect(next.draft.endAt).toBe(before.draft.endAt)
    expect(next.draft.issues).toStrictEqual([])
    expect(next.included).toBe(true)
  })

  it('повторный разбор того же файла даёт те же ключи', async () => {
    const hook = await renderImport()
    const file = calendarFile()

    act(() => {
      hook.result.current.selectFile(file)
    })
    await act(async () => {
      await hook.result.current.parseFile()
    })
    const first = hook.result.current.rows.map((row) => row.idempotencyKey)

    await act(async () => {
      await hook.result.current.parseFile()
    })
    const second = hook.result.current.rows.map((row) => row.idempotencyKey)

    expect(second).toStrictEqual(first)
    expect(new Set(first).size).toBe(first.length)
  })

  it('409 → слот → applySlot берёт длительность услуги, а не события', async () => {
    const calls: BookingCall[] = []
    const slot = {
      startAt: '2026-11-20T07:00:00.000Z',
      endAt: '2026-11-20T08:00:00.000Z',
    }
    let ivanFailed = false
    let secondStarted = false
    let releaseSecond: () => void = () => {}
    const secondGate = new Promise<void>((resolve) => {
      releaseSecond = resolve
    })

    server.use(
      http.post(BOOKINGS_PATH, async ({ request }) => {
        const body = (await request.json()) as {
          guestFirstName: string
          startAt: string
          endAt: string
          idempotencyKey: string
        }

        calls.push({
          key: body.idempotencyKey,
          firstName: body.guestFirstName,
          startAt: body.startAt,
          endAt: body.endAt,
        })

        if (body.guestFirstName === 'Иван' && !ivanFailed) {
          ivanFailed = true

          return HttpResponse.json(
            problemDetails(409, { errorCodes: ['booking.add.overlap'] }),
            { status: 409 }
          )
        }

        // Второй POST Ивана держим открытым: пока он в полёте, отчёт обязан
        // остаться на экране, иначе фокус успевает перескочить в предпросмотр.
        if (body.guestFirstName === 'Иван' && ivanFailed) {
          secondStarted = true
          await secondGate
        }

        return created(`booking-${calls.length}`)
      }),
      http.get(SLOTS_PATH, () =>
        HttpResponse.json({
          barberProfileId: 'profile-1',
          salonId: 'salon-1',
          barberServiceId: 'svc-haircut',
          hairServiceId: 'hair-haircut',
          date: '2026-11-12',
          timeZoneId: 'Asia/Yekaterinburg',
          dayStart: '2026-11-12T05:00:00.000Z',
          dayEnd: '2026-11-12T15:00:00.000Z',
          durationMinutes: 60,
          isWorkingDay: true,
          slots: [slot],
        })
      )
    )

    const hook = await renderImport()

    await parseCalendar(hook)

    const single = findRow(hook, 'Иван')

    // В календаре визит 45 минут, услуга — 60.
    expect(single.draft.eventDurationMinutes).toBe(45)

    await act(async () => {
      await hook.result.current.createRows()
    })

    const failed = findRow(hook, 'Иван')

    expect(failed.status).toBe('failed')
    expect(failed.overlap).toBe(true)
    expect(failed.slots).toStrictEqual([slot])

    let applyPromise: Promise<void> = Promise.resolve()

    act(() => {
      applyPromise = hook.result.current.applySlot(failed.draft.line, slot)
    })

    await waitFor(() => {
      expect(secondStarted).toBe(true)
    })

    // Подстановка слота из отчёта не должна возвращать на предпросмотр.
    expect(hook.result.current.stage).toBe('result')

    await act(async () => {
      releaseSecond()
      await applyPromise
    })

    const patched = findRow(hook, 'Иван')

    expect(patched.status).toBe('created')
    expect(patched.draft.eventDurationMinutes).toBeNull()
    // Конец — слот + 60 минут услуги, а не 45 минут события.
    expect(patched.draft.endAt).toBe('2026-11-20T08:00:00.000Z')
    expect(calls[calls.length - 1].endAt).toBe('2026-11-20T08:00:00.000Z')
  })

  it('continueImport возвращает в preview и выбирает следующие строки', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call, index) => created(`booking-${index}`))
    )

    const hook = await renderImport()
    const file = calendarFile()
    const read = vi.spyOn(File.prototype, 'arrayBuffer')

    act(() => {
      hook.result.current.selectFile(file)
    })
    await act(async () => {
      await hook.result.current.parseFile()
    })

    const skippedRow = hook.result.current.rows[2]

    act(() => {
      hook.result.current.toggleRow(skippedRow.draft.line)
    })

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.summary.created).toBe(2)
    expect(findRow(hook, 'Иван').status).toBe('created')
    expect(hook.result.current.canContinue).toBe(true)
    expect(hook.result.current.progress).toStrictEqual({ done: 2, total: 2 })

    act(() => {
      hook.result.current.continueImport()
    })

    expect(hook.result.current.stage).toBe('preview')
    // Прогресс закончившейся пачки не должен переезжать в следующий предпросмотр.
    expect(hook.result.current.progress).toBeNull()

    const createdRows = hook.result.current.rows.filter(
      (row) => row.status === 'created'
    )

    expect(createdRows.every((row) => !row.included)).toBe(true)
    expect(hook.result.current.rows[2].included).toBe(true)
    expect(hook.result.current.summary.selected).toBe(1)
    // Файл заново не читается: строка уже в памяти.
    expect(read).toHaveBeenCalledTimes(1)
  })

  it('reviewRows возвращает в preview, сохраняя строки и выбор', async () => {
    const calls: BookingCall[] = []

    server.use(
      bookingHandler(calls, (_call, index) => created(`booking-${index}`))
    )

    const hook = await renderImport()

    await parseCalendar(hook)

    const skippedRow = hook.result.current.rows[2]

    act(() => {
      hook.result.current.toggleRow(skippedRow.draft.line)
    })

    await act(async () => {
      await hook.result.current.createRows()
    })

    expect(hook.result.current.stage).toBe('result')

    const before = hook.result.current.rows.map((row) => ({
      line: row.draft.line,
      included: row.included,
      status: row.status,
    }))

    expect(hook.result.current.progress).toStrictEqual({ done: 2, total: 2 })

    act(() => {
      hook.result.current.reviewRows()
    })

    expect(hook.result.current.stage).toBe('preview')
    expect(hook.result.current.progress).toBeNull()
    expect(
      hook.result.current.rows.map((row) => ({
        line: row.draft.line,
        included: row.included,
        status: row.status,
      }))
    ).toStrictEqual(before)
    // Созданные строки не сбрасываются, снятая галочка остаётся снятой.
    expect(
      hook.result.current.rows.filter((row) => row.status === 'created')
    ).toHaveLength(2)
    expect(hook.result.current.rows[2].included).toBe(false)
  })

  it('размонтирование во время parseFile не пишет в консоль', async () => {
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
    const hook = await renderImport()
    let promise: Promise<void> = Promise.resolve()

    act(() => {
      hook.result.current.selectFile(calendarFile())
    })
    act(() => {
      promise = hook.result.current.parseFile()
    })

    hook.unmount()
    await act(async () => {
      await promise
    })

    expect(errorSpy).not.toHaveBeenCalled()
  })

  it('setPeriod и setSource сбрасывают разбор, сохраняя файл и текст', async () => {
    const hook = await renderImport()

    act(() => {
      hook.result.current.setText('завтра')
    })
    await parseCalendar(hook)

    expect(hook.result.current.stage).toBe('preview')

    act(() => {
      hook.result.current.setPeriod(90)
    })

    expect(hook.result.current.stage).toBe('form')
    expect(hook.result.current.rows).toStrictEqual([])
    expect(hook.result.current.skipped).toStrictEqual([])
    expect(hook.result.current.failure).toBeNull()
    expect(hook.result.current.periodDays).toBe(90)
    expect(hook.result.current.file).not.toBeNull()

    // Без аргумента разбор повторяет сохранённый файл.
    await act(async () => {
      await hook.result.current.parseFile()
    })

    expect(hook.result.current.stage).toBe('preview')

    act(() => {
      hook.result.current.setSource('text')
    })

    expect(hook.result.current.stage).toBe('form')
    expect(hook.result.current.rows).toStrictEqual([])
    expect(hook.result.current.skipped).toStrictEqual([])
    expect(hook.result.current.file).not.toBeNull()
    expect(hook.result.current.periodDays).toBe(90)
    expect(hook.result.current.text).toBe('завтра')
  })

  it('reset выключает всё и возвращает период 30', async () => {
    const hook = await renderImport()

    act(() => {
      hook.result.current.setPeriod(90)
    })
    act(() => {
      hook.result.current.setText('завтра')
    })
    await parseCalendar(hook)

    act(() => {
      hook.result.current.reset()
    })

    expect(hook.result.current.periodDays).toBe(30)
    expect(hook.result.current.file).toBeNull()
    expect(hook.result.current.text).toBe('')
    expect(hook.result.current.stage).toBe('form')
    expect(hook.result.current.rows).toStrictEqual([])
    expect(hook.result.current.skipped).toStrictEqual([])
    expect(hook.result.current.failure).toBeNull()
    expect(hook.result.current.isParsing).toBe(false)
  })
})
