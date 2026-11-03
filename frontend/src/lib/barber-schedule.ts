import { ApiError, apiRequest } from '@/lib/api'
import type { DayOfWeek } from '@/lib/barber-profile-labels'
import { addDays } from '@/lib/barber-slots'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isBarberSchedule } from '@/lib/response-guards'

/**
 * Расписание барбера: недельный шаблон, исключения на даты и отпуска.
 *
 * Время недели и исключений — локальное для салона (`TimeOnly`), поэтому его нельзя
 * пересчитывать в пояс браузера. Отпуска, наоборот, приходят UTC-инстантами: их
 * показываем в поясе основного салона.
 */

export type BarberScheduleDay = {
  dayOfWeek: DayOfWeek
  isDayOff: boolean
  /** `TimeOnly` из API (`10:00:00`), не ISO. */
  startTime: string
  endTime: string
}

export type BarberScheduleException = {
  /** Локальная дата салона `YYYY-MM-DD`. */
  date: string
  isDayOff: boolean
  startTime: string
  endTime: string
  reason: string | null
}

export type BarberScheduleSalon = {
  salonId: string
  name: string
  city: string
  timeZoneId: string
  isPrimary: boolean
  /** Всегда семь дней, понедельник первый. */
  weekly: BarberScheduleDay[]
  exceptions: BarberScheduleException[]
}

export type BarberScheduleTimeOff = {
  id: string
  startAt: string
  endAt: string
  reason: string | null
}

export type BarberSchedule = {
  barberProfileId: string
  salons: BarberScheduleSalon[]
  timeOffs: BarberScheduleTimeOff[]
}

export type WorkingDayInput = {
  dayOfWeek: DayOfWeek
  isDayOff: boolean
  startTime: string
  endTime: string
}

export type ScheduleExceptionInput = {
  isDayOff: boolean
  startTime: string
  endTime: string
  reason: string | null
}

export type TimeOffInput = {
  startAt: string
  endAt: string
  reason: string | null
}

/** Порядок дней в редакторе: неделя начинается с понедельника. */
export const WEEK_ORDER: readonly DayOfWeek[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
]

export const DAY_LABELS: Record<DayOfWeek, string> = {
  Monday: 'Понедельник',
  Tuesday: 'Вторник',
  Wednesday: 'Среда',
  Thursday: 'Четверг',
  Friday: 'Пятница',
  Saturday: 'Суббота',
  Sunday: 'Воскресенье',
}

export const DAY_SHORT_LABELS: Record<DayOfWeek, string> = {
  Monday: 'Пн',
  Tuesday: 'Вт',
  Wednesday: 'Ср',
  Thursday: 'Чт',
  Friday: 'Пт',
  Saturday: 'Сб',
  Sunday: 'Вс',
}

export const DEFAULT_START_TIME = '10:00:00'
export const DEFAULT_END_TIME = '20:00:00'

export function fetchBarberSchedule(
  barberProfileId: string,
  signal?: AbortSignal
): Promise<BarberSchedule> {
  return apiRequest<BarberSchedule>(
    `/barber-profiles/${barberProfileId}/schedule`,
    signal ? { signal } : {},
    { validate: isBarberSchedule }
  )
}

/** Полная замена недели: ровно семь дней, по одному на день недели. */
export async function replaceWorkingSchedules(
  barberProfileId: string,
  salonId: string,
  days: readonly WorkingDayInput[]
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/salons/${salonId}/working-schedules`,
    {
      method: 'PUT',
      body: JSON.stringify({ days }),
    }
  )
}

/** Поставить или переписать исключение на дату. */
export async function setScheduleException(
  barberProfileId: string,
  salonId: string,
  date: string,
  input: ScheduleExceptionInput
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/salons/${salonId}/schedule-exceptions/${date}`,
    {
      method: 'PUT',
      body: JSON.stringify(input),
    }
  )
}

export async function removeScheduleException(
  barberProfileId: string,
  salonId: string,
  date: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/salons/${salonId}/schedule-exceptions/${date}`,
    { method: 'DELETE' }
  )
}

/** Отпуск общий для профиля; даты приходят уже UTC-инстантами. */
export async function addTimeOff(
  barberProfileId: string,
  input: TimeOffInput
): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberProfileId}/time-offs`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export async function removeTimeOff(
  barberProfileId: string,
  timeOffId: string
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/time-offs/${timeOffId}`,
    { method: 'DELETE' }
  )
}

/** `10:00:00` → `10:00` для `<input type="time">`. */
export function toTimeInputValue(value: string): string {
  const match = /^(\d{2}):(\d{2})/.exec(value)

  return match ? `${match[1]}:${match[2]}` : ''
}

/** `10:00` → `10:00:00`; пустое значение станет `00:00:00`. */
export function fromTimeInputValue(value: string): string {
  return `${value || '00:00'}:00`
}

/** Все семь дней выключены: исходное состояние пустого расписания. */
export function emptyWeek(): BarberScheduleDay[] {
  return WEEK_ORDER.map((dayOfWeek) => ({
    dayOfWeek,
    isDayOff: true,
    startTime: DEFAULT_START_TIME,
    endTime: DEFAULT_END_TIME,
  }))
}

/** Будни 10:00–20:00, выходные — выходные: кнопка «Заполнить будни». */
export function weekdayTemplate(): BarberScheduleDay[] {
  return WEEK_ORDER.map((dayOfWeek) => ({
    dayOfWeek,
    isDayOff: dayOfWeek === 'Saturday' || dayOfWeek === 'Sunday',
    startTime: DEFAULT_START_TIME,
    endTime: DEFAULT_END_TIME,
  }))
}

export function weeklyDaysToInput(
  days: readonly BarberScheduleDay[]
): WorkingDayInput[] {
  return WEEK_ORDER.map((dayOfWeek) => {
    const day = days.find((candidate) => candidate.dayOfWeek === dayOfWeek)
    const isDayOff = day?.isDayOff ?? true

    // В выходном дне время — `00:00:00`; если барбер включит день обратно,
    // поля должны начать с осмысленных 10:00–20:00, а не с невалидных 00:00.
    return {
      dayOfWeek,
      isDayOff,
      startTime: isDayOff
        ? DEFAULT_START_TIME
        : (day?.startTime ?? DEFAULT_START_TIME),
      endTime: isDayOff
        ? DEFAULT_END_TIME
        : (day?.endTime ?? DEFAULT_END_TIME),
    }
  })
}

/** Сравнение недели для кнопки «Сохранить»: порядок и значения важны. */
export function isSameWeek(
  left: readonly WorkingDayInput[],
  right: readonly WorkingDayInput[]
): boolean {
  return WEEK_ORDER.every((dayOfWeek) => {
    const a = left.find((day) => day.dayOfWeek === dayOfWeek)
    const b = right.find((day) => day.dayOfWeek === dayOfWeek)

    return (
      (a?.isDayOff ?? true) === (b?.isDayOff ?? true)
      && (a?.startTime ?? '') === (b?.startTime ?? '')
      && (a?.endTime ?? '') === (b?.endTime ?? '')
    )
  })
}

/**
 * Локальная полночь даты в поясе салона → UTC-инстант.
 *
 * `end` — та же полночь следующего дня: отпуск хранит исключающую верхнюю границу,
 * поэтому «с 1 по 10» отправляется как `1 00:00` … `11 00:00`. Смещение считается
 * через `Intl`, а не через `Date`: браузер может жить в другом поясе.
 */
export function localDateToUtcIso(
  date: string,
  timeZoneId: string,
  boundary: 'start' | 'end' = 'start'
): string {
  const target = boundary === 'end' ? addDays(date, 1) : date
  const [year, month, day] = target.split('-').map(Number)

  if (!year || !month || !day) {
    return `${target}T00:00:00.000Z`
  }

  const localAsUtc = Date.UTC(year, month - 1, day, 0, 0, 0)

  try {
    let offset = timeZoneOffsetMs(new Date(localAsUtc), timeZoneId)
    let utc = localAsUtc - offset

    // Один уточняющий шаг покрывает переход на летнее/зимнее время.
    offset = timeZoneOffsetMs(new Date(utc), timeZoneId)
    utc = localAsUtc - offset

    return new Date(utc).toISOString()
  } catch {
    return `${target}T00:00:00.000Z`
  }
}

function timeZoneOffsetMs(date: Date, timeZoneId: string): number {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: timeZoneId,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(date)

  const values = Object.fromEntries(
    parts.map((part) => [part.type, part.value])
  )

  const asUtc = Date.UTC(
    Number(values.year),
    Number(values.month) - 1,
    Number(values.day),
    Number(values.hour),
    Number(values.minute),
    Number(values.second)
  )

  return asUtc - date.getTime()
}

/** «15 ноября 2026» для календарной даты, без сдвига пояса. */
export function formatScheduleDate(date: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date)

  if (!match) {
    return date
  }

  return new Intl.DateTimeFormat('ru-RU', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  })
    .format(new Date(`${date}T00:00:00Z`))
    .replace(/\s*г\.$/, '')
}

/** «15 ноября 2026» для UTC-инстанта в поясе салона. */
export function formatZonedDate(iso: string, timeZoneId: string): string {
  try {
    return new Intl.DateTimeFormat('ru-RU', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: timeZoneId,
    })
      .format(new Date(iso))
      .replace(/\s*г\.$/, '')
  } catch {
    return iso
  }
}

/** «с 1 по 10 ноября 2026» из исключающего интервала UTC. */
export function formatTimeOffRange(
  startAt: string,
  endAt: string,
  timeZoneId: string
): string {
  const start = formatZonedDate(startAt, timeZoneId)
  const end = formatZonedDate(
    new Date(new Date(endAt).getTime() - 1).toISOString(),
    timeZoneId
  )

  return start === end ? start : `${start} — ${end}`
}

/**
 * Ошибки расписания: публичные коды важнее английского `detail`.
 */
export function describeScheduleError(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.hasCode('barberSalon.replaceSchedule.days')) {
    return 'Нужно заполнить все семь дней недели.'
  }

  if (error.hasCode('barberSalon.replaceSchedule.duplicateDay')) {
    return 'День недели указан дважды. Обновите страницу и повторите.'
  }

  if (
    error.hasCode('barberSalon.replaceSchedule.timeRange')
    || error.hasCode('barberSalon.setScheduleException.timeRange')
  ) {
    return 'Конец рабочего дня должен быть позже начала.'
  }

  if (error.hasCode('barberSalon.setScheduleException.reasonTooLong')) {
    return 'Пометка слишком длинная — максимум 500 символов.'
  }

  if (error.hasCode('barberSalon.removeScheduleException.notFound')) {
    return 'Это исключение уже удалено.'
  }

  if (error.hasCode('barberProfile.addTimeOff.overlap')) {
    return 'Этот период пересекается с другим отпуском.'
  }

  if (error.hasCode('barberProfile.addTimeOff.period')) {
    return 'Конец периода должен быть позже начала.'
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return 'Расписание доступно только владельцу профиля.'
  }

  if (error.status === 404) {
    return 'Профиль или салон не найдены. Обновите страницу.'
  }

  return describeApiFailure(error, fallback)
}
