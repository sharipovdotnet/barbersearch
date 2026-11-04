import { http, HttpResponse, type JsonBodyType } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  addTimeOff,
  describeScheduleError,
  fetchBarberSchedule,
  formatScheduleDate,
  localDateToUtcIso,
  removeScheduleException,
  removeTimeOff,
  replaceWorkingSchedules,
  setScheduleException,
  weeklyDaysToInput,
  type BarberSchedule,
} from '@/lib/barber-schedule'
import { problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const SCHEDULE_URL = `/api/barber-profiles/${PROFILE_ID}/schedule`

const WEEK = [
  ['Monday', false, '10:00:00', '20:00:00'],
  ['Tuesday', false, '10:00:00', '20:00:00'],
  ['Wednesday', false, '10:00:00', '20:00:00'],
  ['Thursday', false, '10:00:00', '20:00:00'],
  ['Friday', false, '10:00:00', '20:00:00'],
  ['Saturday', true, '00:00:00', '00:00:00'],
  ['Sunday', true, '00:00:00', '00:00:00'],
] as const

const SCHEDULE: BarberSchedule = {
  barberProfileId: PROFILE_ID,
  salons: [
    {
      salonId: SALON_ID,
      name: 'Барбершоп',
      city: 'Екатеринбург',
      timeZoneId: 'Asia/Yekaterinburg',
      isPrimary: true,
      weekly: WEEK.map(([dayOfWeek, isDayOff, startTime, endTime]) => ({
        dayOfWeek,
        isDayOff,
        startTime,
        endTime,
      })),
      exceptions: [
        {
          date: '2026-11-15',
          isDayOff: true,
          startTime: '00:00:00',
          endTime: '00:00:00',
          reason: 'Личные дела',
        },
      ],
    },
  ],
  timeOffs: [
    {
      id: '33333333-3333-4333-8333-333333333333',
      startAt: '2026-12-01T00:00:00+00:00',
      endAt: '2026-12-03T00:00:00+00:00',
      reason: 'Отпуск',
    },
  ],
}

function serveSchedule(body: JsonBodyType) {
  server.use(http.get(SCHEDULE_URL, () => HttpResponse.json(body)))
}

describe('fetchBarberSchedule', () => {
  it('читает расписание и принимает лишние поля', async () => {
    const requested: string[] = []

    server.use(
      http.get(SCHEDULE_URL, ({ request }) => {
        requested.push(request.url)

        return HttpResponse.json({ ...SCHEDULE, total: 1 })
      })
    )

    const schedule = await fetchBarberSchedule(PROFILE_ID)

    expect(schedule.salons).toHaveLength(1)
    expect(schedule.salons[0].weekly).toHaveLength(7)
    expect(new URL(requested[0]).pathname).toBe(SCHEDULE_URL)
    expect(new URL(requested[0]).search).toBe('')
  })

  it('не пропускает неделю из шести дней', async () => {
    serveSchedule({
      ...SCHEDULE,
      salons: [
        {
          ...SCHEDULE.salons[0],
          weekly: SCHEDULE.salons[0].weekly.slice(0, 6),
        },
      ],
    })

    await expect(fetchBarberSchedule(PROFILE_ID)).rejects.toBeInstanceOf(
      ApiError
    )
    await expect(fetchBarberSchedule(PROFILE_ID)).rejects.toThrow(
      'Сервер вернул неожиданный ответ.'
    )
  })
})

describe('мутации расписания', () => {
  it('PUT недели отправляет семь дней и CSRF-заголовок', async () => {
    let method = ''
    let header: string | null = null
    let body: unknown = null

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/working-schedules`,
        async ({ request }) => {
          method = request.method
          header = request.headers.get('X-CSRF-TOKEN')
          body = await request.json()

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    await replaceWorkingSchedules(
      PROFILE_ID,
      SALON_ID,
      WEEK.map(([dayOfWeek, isDayOff, startTime, endTime]) => ({
        dayOfWeek,
        isDayOff,
        startTime,
        endTime,
      }))
    )

    expect(method).toBe('PUT')
    expect(header).toBe('csrf-token')
    expect(body).toStrictEqual({
      days: WEEK.map(([dayOfWeek, isDayOff, startTime, endTime]) => ({
        dayOfWeek,
        isDayOff,
        startTime,
        endTime,
      })),
    })
  })

  it('PUT исключения кодирует дату в пути', async () => {
    let url = ''
    let body: unknown = null

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-15`,
        async ({ request }) => {
          url = request.url
          body = await request.json()

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    await setScheduleException(PROFILE_ID, SALON_ID, '2026-11-15', {
      isDayOff: true,
      startTime: '00:00:00',
      endTime: '00:00:00',
      reason: null,
    })

    expect(new URL(url).pathname).toBe(
      `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-15`
    )
    expect(body).toStrictEqual({
      isDayOff: true,
      startTime: '00:00:00',
      endTime: '00:00:00',
      reason: null,
    })
  })

  it('DELETE исключения и отпуска идут без тела', async () => {
    const calls: string[] = []

    server.use(
      http.delete(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-15`,
        ({ request }) => {
          calls.push(request.method)

          return new HttpResponse(null, { status: 204 })
        }
      ),
      http.delete(
        `/api/barber-profiles/${PROFILE_ID}/time-offs/33333333-3333-4333-8333-333333333333`,
        ({ request }) => {
          calls.push(request.method)

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    await removeScheduleException(PROFILE_ID, SALON_ID, '2026-11-15')
    await removeTimeOff(PROFILE_ID, '33333333-3333-4333-8333-333333333333')

    expect(calls).toEqual(['DELETE', 'DELETE'])
  })

  it('POST отпуска отправляет интервал в UTC', async () => {
    let body: unknown = null

    server.use(
      http.post(
        `/api/barber-profiles/${PROFILE_ID}/time-offs`,
        async ({ request }) => {
          body = await request.json()

          return new HttpResponse(null, { status: 204 })
        }
      )
    )

    await addTimeOff(PROFILE_ID, {
      startAt: '2026-11-30T19:00:00.000Z',
      endAt: '2026-12-02T19:00:00.000Z',
      reason: 'Отпуск',
    })

    expect(body).toStrictEqual({
      startAt: '2026-11-30T19:00:00.000Z',
      endAt: '2026-12-02T19:00:00.000Z',
      reason: 'Отпуск',
    })
  })
})

describe('календарные помощники', () => {
  it('переводит локальную полночь в UTC с учётом пояса', () => {
    expect(localDateToUtcIso('2026-11-01', 'Europe/Berlin')).toBe(
      '2026-10-31T23:00:00.000Z'
    )
    expect(localDateToUtcIso('2026-11-01', 'Asia/Yekaterinburg')).toBe(
      '2026-10-31T19:00:00.000Z'
    )
  })

  it('верхняя граница отпуска — следующая полночь', () => {
    expect(localDateToUtcIso('2026-11-10', 'Europe/Berlin', 'end')).toBe(
      '2026-11-10T23:00:00.000Z'
    )
  })

  it('форматирует календарную дату без сдвига пояса', () => {
    expect(formatScheduleDate('2026-11-15')).toBe('15 ноября 2026')
  })

  it('для выходного дня подставляет осмысленные 10:00–20:00', () => {
    const monday = weeklyDaysToInput([
      {
        dayOfWeek: 'Monday',
        isDayOff: true,
        startTime: '00:00:00',
        endTime: '00:00:00',
      },
    ])[0]

    expect(monday).toMatchObject({
      isDayOff: true,
      startTime: '10:00:00',
      endTime: '20:00:00',
    })
  })
})

describe('describeScheduleError', () => {
  const fallback = 'Не удалось сохранить расписание.'

  it('объясняет неполную неделю', () => {
    const error = new ApiError(
      400,
      problemDetails(400, {
        errorCodes: ['barberSalon.replaceSchedule.days'],
      }),
      'Bad request.'
    )

    expect(describeScheduleError(error, fallback)).toBe(
      'Нужно заполнить все семь дней недели.'
    )
  })

  it('объясняет пересечение отпусков', () => {
    const error = new ApiError(
      409,
      problemDetails(409, {
        errorCodes: ['barberProfile.addTimeOff.overlap'],
      }),
      'Conflict.'
    )

    expect(describeScheduleError(error, fallback)).toBe(
      'Этот период пересекается с другим отпуском.'
    )
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(describeScheduleError(new Error('сеть'), fallback)).toBe(fallback)
  })
})
