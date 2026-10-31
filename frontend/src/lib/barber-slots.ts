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

export type BarberSlotsParams = {
  barberProfileId: string
  /** Локальная дата салона; битую строку сервер вернёт framework-`400`. */
  date: string
  hairServiceId: string | null
  salonId: string
}

const SALON_DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/
const DAY_MS = 24 * 60 * 60 * 1000

/** `YYYY-MM-DD` — единственный формат, который понимает `DateOnly` на сервере. */
export function isSalonDate(value: string): boolean {
  return SALON_DATE_PATTERN.test(value)
}

function dayKey(date: Date, timeZoneId: string): string {
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }

  try {
    return new Intl.DateTimeFormat('en-CA', {
      ...options,
      timeZone: timeZoneId,
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
 * в разных поясах уже относится к разным календарным дням.
 */
export function todayInTimeZone(timeZoneId: string): string {
  return dayKey(new Date(), timeZoneId)
}

/** Календарная арифметика без часовых поясов: `2026-11-30` + 1 → `2026-12-01`. */
export function addDays(date: string, days: number): string {
  const start = Date.parse(`${date}T00:00:00Z`)

  if (Number.isNaN(start)) {
    return date
  }

  return new Date(start + days * DAY_MS).toISOString().slice(0, 10)
}

/** Один запрос слотов; параметры собираются `URLSearchParams`, чтобы не кодировать вручную. */
export async function fetchBarberSlots(
  params: BarberSlotsParams,
  signal?: AbortSignal
): Promise<BarberSlots> {
  const query = new URLSearchParams({ date: params.date })

  if (params.hairServiceId !== null && params.hairServiceId !== '') {
    query.set('hairServiceId', params.hairServiceId)
  }

  if (params.salonId !== '') {
    query.set('salonId', params.salonId)
  }

  return apiRequest<BarberSlots>(
    `/barbers/${params.barberProfileId}/slots?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isBarberSlots }
  )
}
