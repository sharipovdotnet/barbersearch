import { apiRequest } from '@/lib/api'
import { isBarberSlots } from '@/lib/response-guards'

/**
 * Свободное время барбера на локальную дату салона.
 *
 * Сервер уже вырезал прошедшие слоты, отпуска и активные брони, поэтому клиент
 * ничего не фильтрует. Время приходит UTC-инстантами, а показывать его нужно
 * в поясе салона (`timeZoneId`), иначе в другом поясе «10:00» станет «07:00».
 */

export type BarberSlot = {
  /** Начало слота в UTC. */
  startAt: string
  /** Окончание слота в UTC. */
  endAt: string
}

export type BarberSlots = {
  barberProfileId: string
  salonId: string
  /** `barber_services.id` для будущей брони; `null`, если услуга не выбрана. */
  barberServiceId: string | null
  /** `hair_services.id`; `null`, если услуга не выбрана. */
  hairServiceId: string | null
  /** Локальная дата салона `YYYY-MM-DD`, без времени. */
  date: string
  /** IANA-пояс салона — единственный правильный пояс для показа слотов. */
  timeZoneId: string
  /** UTC-границы локального дня; для показа времени слотов не используются. */
  dayStart: string
  dayEnd: string
  /** Шаг сетки: длительность услуги или 30 минут по умолчанию. */
  durationMinutes: number
  isWorkingDay: boolean
  slots: BarberSlot[]
}

/**
 * Параметры чтения слотов: публичная сетка, собственная сетка кабинета или сетка
 * переноса конкретной брони.
 *
 * Режим брони не принимает `barberProfileId`/`salonId`/`hairServiceId`: сервер берёт
 * их из самой брони, а длительность — из её `EndAt − StartAt`. Так `BarberBooking`
 * без `salonId`/`barberServiceId` не нужно достраивать до публичного запроса.
 */
export type BarberSlotsParams =
  | {
      barberProfileId: string
      /** Локальная дата салона; битую строку сервер вернёт framework-`400`. */
      date: string
      hairServiceId: string | null
      salonId: string
      /**
       * `true` — слоты собственного профиля (`GET api/barber/slots`): кабинет работает
       * и до публикации, поэтому сервер не требует активного и подтверждённого профиля.
       */
      own?: boolean
      bookingId?: never
    }
  | {
      date: string
      /** `GET api/bookings/{id}/reschedule-slots?date=…`. */
      bookingId: string
      barberProfileId?: never
      hairServiceId?: never
      salonId?: never
      own?: never
    }

const SALON_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/
const DAY_MS = 24 * 60 * 60 * 1000

/**
 * Горизонт переноса в днях.
 *
 * Зеркало серверной `Constants.BOOKING_HORIZON_DAYS`: клиент не читает серверные
 * константы, поэтому число повторено здесь и подписано. Только для `max` нативного
 * поля даты; окончательно границу проверяет сервер (`booking.reschedule.startAt.tooFar`).
 */
export const RESCHEDULE_HORIZON_DAYS = 90

/** `YYYY-MM-DD` — единственный формат, который понимает `DateOnly` на сервере. */
export function isSalonDate(value: string): boolean {
  return SALON_DATE_PATTERN.test(value)
}

function dayKey(date: Date, timeZoneId: string | undefined): string {
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }

  try {
    return new Intl.DateTimeFormat('en-CA', {
      ...options,
      ...(timeZoneId === undefined ? {} : { timeZone: timeZoneId }),
    }).format(date)
  } catch {
    // Битый исторический пояс не должен ронять страницу: считаем в поясе браузера.
    return new Intl.DateTimeFormat('en-CA', options).format(date)
  }
}

/**
 * Сегодняшняя дата в поясе салона.
 *
 * Именно в поясе салона, а не браузера: `en-CA` отдаёт `YYYY-MM-DD`, а `Date`
 * в разных поясах уже относится к разным календарным дням. Без пояса (карточка
 * барбера ещё не загрузилась) считаем в поясе браузера, как `formatTime`.
 */
export function todayInTimeZone(timeZoneId?: string): string {
  return dayKey(new Date(), timeZoneId)
}

/**
 * Календарная дата инстанта в поясе салона (`YYYY-MM-DD`).
 *
 * Нужна диалогу переноса для начального значения поля даты: `startAt` приходит
 * UTC-инстантом, а поле понимает только локальную дату салона. Битый пояс — фолбэк
 * на пояс браузера, битый инстант — пустая строка (запрос слотов не отправится).
 */
export function salonDateOf(iso: string, timeZoneId?: string): string {
  const date = new Date(iso)

  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return dayKey(date, timeZoneId)
}

/** Календарная арифметика без часовых поясов: `2026-11-30` + 1 → `2026-12-01`. */
export function addDays(date: string, days: number): string {
  const start = Date.parse(`${date}T00:00:00Z`)

  if (Number.isNaN(start)) {
    return date
  }

  return new Date(start + days * DAY_MS).toISOString().slice(0, 10)
}

/**
 * Один запрос слотов; параметры собираются `URLSearchParams`, чтобы не кодировать
 * вручную. У режима брони в запросе только дата: профиль, салон и длительность
 * сервер знает из самой брони.
 */
export async function fetchBarberSlots(
  params: BarberSlotsParams,
  signal?: AbortSignal
): Promise<BarberSlots> {
  const query = new URLSearchParams({ date: params.date })

  if (params.bookingId !== undefined) {
    return apiRequest<BarberSlots>(
      `/bookings/${encodeURIComponent(params.bookingId)}/reschedule-slots?${query.toString()}`,
      signal ? { signal } : {},
      { validate: isBarberSlots }
    )
  }

  if (params.hairServiceId !== null && params.hairServiceId !== '') {
    query.set('hairServiceId', params.hairServiceId)
  }

  if (params.salonId !== '') {
    query.set('salonId', params.salonId)
  }

  const path = params.own
    ? `/barber/slots?${query.toString()}`
    : `/barbers/${params.barberProfileId}/slots?${query.toString()}`

  return apiRequest<BarberSlots>(
    path,
    signal ? { signal } : {},
    { validate: isBarberSlots }
  )
}
