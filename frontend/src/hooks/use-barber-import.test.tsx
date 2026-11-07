import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { useBarberImport, type BarberImportController } from '@/hooks/use-barber-import'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

/**
 * Хук переноса: разбор, проверка, последовательная отправка.
 *
 * Сеть — только MSW. Даты в тексте относительные («завтра»): тест не должен
 * зависеть от дня календаря, иначе «в прошлом» появилось бы само.
 */

const OPTIONS_PATH = '/api/barber/booking-options'
const BOOKINGS_PATH = '/api/barber/bookings'
const SLOTS_PATH = '/api/barber/slots'

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
      idempotencyKey: string
    }

    const call: BookingCall = {
      key: body.idempotencyKey,
      firstName: body.guestFirstName,
      startAt: body.startAt,
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
  act(() => {
    hook.result.current.setText(text)
  })
  act(() => {
    hook.result.current.runParse()
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
