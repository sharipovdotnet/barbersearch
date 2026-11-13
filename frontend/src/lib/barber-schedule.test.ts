import { http, HttpResponse, type JsonBodyType } from 'msw'
import { describe, expect, it } from 'vitest'

import { ApiError } from '@/lib/api'
import {
  addTimeOff,
  describeScheduleError,
  fetchBarberSchedule,
  formatScheduleDate,
  localDateToUtcIso,
  readScheduleConflicts,
  removeScheduleException,
  removeTimeOff,
  replaceWorkingSchedules,
  setScheduleException,
  weeklyDaysToInput,
  type BarberSchedule,
  type ScheduleConflict,
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

  it('объясняет таймаут блокировки расписания', () => {
    const error = new ApiError(
      409,
      problemDetails(409, {
        errorCodes: ['barberSchedule.lockTimeout'],
      }),
      'Conflict.'
    )

    expect(describeScheduleError(error, fallback)).toBe(
      'Расписание не сохранено: попробуйте ещё раз.'
    )
  })

  it('не подменяет конфликт расписания общим текстом', () => {
    // Код конфликта обрабатывает диалог, а не эта функция: здесь он обязан
    // провалиться в общий fallback, а не получить собственную ветку.
    const error = new ApiError(
      409,
      problemDetails(409, {
        errorCodes: ['barberSchedule.conflict'],
      }),
      'Conflict.'
    )

    expect(describeScheduleError(error, fallback)).toBe(fallback)
  })

  it('на неизвестной ошибке отдаёт запасной текст', () => {
    expect(describeScheduleError(new Error('сеть'), fallback)).toBe(fallback)
  })
})

const CONFLICT: ScheduleConflict = {
  id: '9e100000-0000-4000-8000-000000000031',
  startAt: '2026-11-20T09:00:00+00:00',
  endAt: '2026-11-20T09:30:00+00:00',
  timeZoneId: 'Asia/Yekaterinburg',
  customerName: 'Иван Петров',
  serviceName: 'Стрижка',
  status: 'Confirmed',
}

/** `409 barberSchedule.conflict`; `body` дописывается поверх корректного тела. */
function conflictError(
  conflicts: unknown,
  options: { status?: number; codes?: string[] } = {}
): ApiError {
  return new ApiError(
    options.status ?? 409,
    {
      ...problemDetails(options.status ?? 409, {
        errorCodes: options.codes ?? ['barberSchedule.conflict'],
      }),
      conflicts,
    },
    'Conflict.'
  )
}

describe('readScheduleConflicts', () => {
  it('читает список из 409 barberSchedule.conflict', () => {
    expect(readScheduleConflicts(conflictError([CONFLICT]))).toStrictEqual([
      CONFLICT,
    ])
  })

  it('на 409 без расширения conflicts отдаёт null', () => {
    expect(readScheduleConflicts(conflictError(undefined))).toBeNull()
  })

  it('на conflicts не-массив отдаёт null', () => {
    expect(readScheduleConflicts(conflictError({ items: [CONFLICT] }))).toBeNull()
    expect(readScheduleConflicts(conflictError('conflicts'))).toBeNull()
  })

  it('на запись без timeZoneId отдаёт null: время негде показать', () => {
    const { timeZoneId, ...withoutZone } = CONFLICT

    expect(timeZoneId).toBe('Asia/Yekaterinburg')
    expect(readScheduleConflicts(conflictError([withoutZone]))).toBeNull()
  })

  it('на неизвестный статус брони отдаёт null', () => {
    expect(
      readScheduleConflicts(conflictError([{ ...CONFLICT, status: 'Archived' }]))
    ).toBeNull()
  })

  it('на пустой список отдаёт null: в диалоге нечего объяснять', () => {
    expect(readScheduleConflicts(conflictError([]))).toBeNull()
  })

  it('не читает другие статусы и другие коды', () => {
    expect(
      readScheduleConflicts(conflictError([CONFLICT], { status: 400 }))
    ).toBeNull()
    expect(
      readScheduleConflicts(conflictError([CONFLICT], { codes: ['other.code'] }))
    ).toBeNull()
  })

  it('на сетевую ошибку и обычную ошибку отдаёт null', () => {
    expect(readScheduleConflicts(new Error('сеть'))).toBeNull()
    expect(readScheduleConflicts(null)).toBeNull()
  })
})

describe('force в мутациях расписания', () => {
  it('не добавляет параметр в обычную попытку', async () => {
    let search: string | null = null

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/working-schedules`,
        ({ request }) => {
          search = new URL(request.url).search

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

    expect(search).toBe('')
  })

  it('шлёт ?force=true для недели, исключения и отпуска', async () => {
    const searches: string[] = []

    server.use(
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/working-schedules`,
        ({ request }) => {
          searches.push(new URL(request.url).search)

          return new HttpResponse(null, { status: 204 })
        }
      ),
      http.put(
        `/api/barber-profiles/${PROFILE_ID}/salons/${SALON_ID}/schedule-exceptions/2026-11-15`,
        ({ request }) => {
          searches.push(new URL(request.url).search)

          return new HttpResponse(null, { status: 204 })
        }
      ),
      http.post(
        `/api/barber-profiles/${PROFILE_ID}/time-offs`,
        ({ request }) => {
          searches.push(new URL(request.url).search)

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
      })),
      true
    )
    await setScheduleException(
      PROFILE_ID,
      SALON_ID,
      '2026-11-15',
      {
        isDayOff: true,
        startTime: '00:00:00',
        endTime: '00:00:00',
        reason: null,
      },
      true
    )
    await addTimeOff(
      PROFILE_ID,
      {
        startAt: '2026-11-30T19:00:00.000Z',
        endAt: '2026-12-02T19:00:00.000Z',
        reason: null,
      },
      true
    )

    expect(searches).toStrictEqual(['?force=true', '?force=true', '?force=true'])
  })
})
