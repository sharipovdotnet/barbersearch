import { getLocale, localeTag, t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import type { DayOfWeek } from '@/lib/barber-profile-labels'
import { addDays } from '@/lib/barber-slots'
import type { BookingStatus } from '@/lib/bookings'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isBarberSchedule, isScheduleConflicts } from '@/lib/response-guards'

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

/**
 * Визит, который после правки расписания оказался вне рабочих часов.
 *
 * Приходит только в расширении `conflicts` ответа `409 barberSchedule.conflict`.
 * Телефона, email и цены в контракте нет: диалогу хватает времени, имени и услуги.
 */
export type ScheduleConflict = {
  id: string
  startAt: string
  endAt: string
  /** Пояс салона записи: время в диалоге показывается именно в нём. */
  timeZoneId: string
  customerName: string
  serviceName: string
  status: BookingStatus
}

/** Что именно правит барбер: от этого зависит заголовок диалога конфликта. */
export type ScheduleWriteKind = 'hours' | 'timeOff'

/**
 * Визиты, из-за которых сервер отказался сохранять расписание.
 *
 * `null` означает «тело не распознано», а не «конфликтов нет»: другой статус,
 * другой код, гонка или старая версия сервера. Пустой список — тоже `null`:
 * `409` без единого визита нечего объяснять в диалоге.
 */
export function readScheduleConflicts(
  error: unknown
): ScheduleConflict[] | null {
  if (
    !(error instanceof ApiError) ||
    error.status !== 409 ||
    !error.hasCode('barberSchedule.conflict')
  ) {
    return null
  }

  const conflicts = error.problem?.conflicts

  if (!isScheduleConflicts(conflicts) || conflicts.length === 0) {
    return null
  }

  return conflicts
}

/** `?force=true` только после подтверждения: обычный путь и его URL не меняются. */
function forceQuery(force: boolean): string {
  return force ? '?force=true' : ''
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

/**
 * Полная замена недели: ровно семь дней, по одному на день недели.
 *
 * `force` — осознанное подтверждение конфликта: сервер сохранит правку, даже если
 * активные визиты выйдут за новые часы. Обычный путь отправляет запрос без параметра.
 */
export async function replaceWorkingSchedules(
  barberProfileId: string,
  salonId: string,
  days: readonly WorkingDayInput[],
  force = false
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/salons/${salonId}/working-schedules${forceQuery(force)}`,
    {
      method: 'PUT',
      body: JSON.stringify({ days }),
    }
  )
}

/**
 * Поставить или переписать исключение на дату.
 *
 * `force` — подтверждение конфликта: визиты этой даты остаются в силе.
 */
export async function setScheduleException(
  barberProfileId: string,
  salonId: string,
  date: string,
  input: ScheduleExceptionInput,
  force = false
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/salons/${salonId}/schedule-exceptions/${date}${forceQuery(force)}`,
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

/**
 * Отпуск общий для профиля; даты приходят уже UTC-инстантами.
 *
 * `force` — подтверждение конфликта: визиты внутри периода остаются в силе.
 */
export async function addTimeOff(
  barberProfileId: string,
  input: TimeOffInput,
  force = false
): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber-profiles/${barberProfileId}/time-offs${forceQuery(force)}`,
    {
      method: 'POST',
      body: JSON.stringify(input),
    }
  )
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

  return new Intl.DateTimeFormat(localeTag(getLocale()), {
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
    return new Intl.DateTimeFormat(localeTag(getLocale()), {
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
    return t('Нужно заполнить все семь дней недели.')
  }

  if (error.hasCode('barberSalon.replaceSchedule.duplicateDay')) {
    return t('День недели указан дважды. Обновите страницу и повторите.')
  }

  if (
    error.hasCode('barberSalon.replaceSchedule.timeRange')
    || error.hasCode('barberSalon.setScheduleException.timeRange')
  ) {
    return t('Конец рабочего дня должен быть позже начала.')
  }

  if (error.hasCode('barberSalon.setScheduleException.reasonTooLong')) {
    return t('Пометка слишком длинная — максимум 500 символов.')
  }

  if (error.hasCode('barberSalon.removeScheduleException.notFound')) {
    return t('Это исключение уже удалено.')
  }

  if (error.hasCode('barberProfile.addTimeOff.overlap')) {
    return t('Этот период пересекается с другим отпуском.')
  }

  if (error.hasCode('barberProfile.addTimeOff.period')) {
    return t('Конец периода должен быть позже начала.')
  }

  // Код блокировки появляется, когда параллельная запись держит advisory-блокировку
  // барбера дольше `lock_timeout`: это повторяемая ошибка, а не конфликт расписания.
  if (error.hasCode('barberSchedule.lockTimeout')) {
    return t('Расписание не сохранено: попробуйте ещё раз.')
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return t('Расписание доступно только владельцу профиля.')
  }

  if (error.status === 404) {
    return t('Профиль или салон не найдены. Обновите страницу.')
  }

  return describeApiFailure(error, fallback)
}
