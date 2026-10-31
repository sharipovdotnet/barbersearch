import { apiRequest } from '@/lib/api'
import type { BarberGrade } from '@/lib/barber-grade'
import type {
  DayOfWeek,
  Language,
  PaymentMethod,
  ServiceCategory,
} from '@/lib/barber-profile-labels'
import { isBarberProfile } from '@/lib/response-guards'

/**
 * Карточка барбера — единственный источник человекочитаемых данных о брони.
 *
 * В `BookingResponse` лежат только `barberProfileId`, `barberServiceId` и `salonId`,
 * поэтому имя мастера, название услуги, салон и его часовой пояс берутся отсюда.
 * Один запрос на барбера закрывает сразу все его брони, а кэш живёт на уровне модуля:
 * кабинет и карточка барбера делят одни и те же данные.
 */

/** Услуга барбера в карточке: цена и длительность конкретного мастера. */
export type BarberProfileService = {
  /** Строка `barber_services` — её ждёт `POST /api/bookings` (R-20). */
  barberServiceId: string
  /** Услуга справочника — её ждут поиск и слоты (R-20). */
  hairServiceId: string
  name: string
  category: ServiceCategory
  defaultDurationMinutes: number
  price: number
  durationMinutes: number
  isActive: boolean
}

/** Недельное расписание пары (барбер, салон). */
export type WorkingSchedule = {
  id: string
  dayOfWeek: DayOfWeek
  /** `TimeOnly` из API (`09:00:00`), не ISO; для показа — `formatScheduleTime`. */
  startTime: string
  endTime: string
  /** Выходной: время в ответе равно `00:00:00` и не показывается. */
  isDayOff: boolean
}

export type BarberProfileSalon = {
  id: string
  salonId: string
  name: string
  address: string
  city: string
  country: string
  timeZoneId: string
  isPrimary: boolean
  workingSchedules: WorkingSchedule[]
}

export type BarberProfileSpecialization = {
  id: string
  specializationId: string
  name: string
  description: string | null
}

/** Публичная карточка: только поля без контактов владельца профиля. */
export type BarberProfile = {
  id: string
  userId: string
  displayName: string
  grade: BarberGrade
  bio: string | null
  yearsOfExperience: number
  languages: Language[]
  certificatesAndTraining: string[]
  rating: number | null
  reviewsCount: number
  achievements: string[]
  paymentMethods: PaymentMethod[]
  communicationFormat: string[]
  goodies: string[]
  latePolicy: string
  isActive: boolean
  isVerified: boolean
  services: BarberProfileService[]
  salons: BarberProfileSalon[]
  specializations: BarberProfileSpecialization[]
}

const profiles = new Map<string, BarberProfile>()
const inFlight = new Map<string, Promise<BarberProfile>>()

/** Уже загруженная карточка — для синхронной отрисовки до ответа сети. */
export function readCachedBarberProfile(id: string): BarberProfile | null {
  return profiles.get(id) ?? null
}

/**
 * Свежий запрос карточки с записью в общий кэш.
 *
 * Нужен странице барбера: ей важен `signal`, чтобы отменить запрос при смене id,
 * а `loadBarberProfile` дедуплицирует запросы кабинета и отменять их нельзя.
 */
export async function fetchBarberProfile(
  id: string,
  signal?: AbortSignal
): Promise<BarberProfile> {
  const profile = await apiRequest<BarberProfile>(
    `/barbers/${id}`,
    signal ? { signal } : {},
    { validate: isBarberProfile }
  )

  // Кэшируем только провалидированную карточку: битый ответ не должен жить в Map.
  profiles.set(profile.id, profile)
  return profile
}

/** Загрузка с дедупликацией: две брони одного барбера дают один запрос. */
export function loadBarberProfile(id: string): Promise<BarberProfile> {
  const cached = profiles.get(id)
  if (cached) {
    return Promise.resolve(cached)
  }

  const pending = inFlight.get(id)
  if (pending) {
    return pending
  }

  const request = fetchBarberProfile(id)
    .then((profile) => {
      inFlight.delete(id)
      return profile
    })
    .catch((error: unknown) => {
      inFlight.delete(id)
      throw error
    })

  inFlight.set(id, request)
  return request
}

/** Услуга брони в карточке барбера; `null`, если услугу убрали из прайса. */
export function findBookingService(
  profile: BarberProfile | undefined,
  barberServiceId: string
): BarberProfileService | null {
  return (
    profile?.services.find(
      (service) => service.barberServiceId === barberServiceId
    ) ?? null
  )
}

/** Салон брони; если его убрали из карточки — основной салон барбера. */
export function findBookingSalon(
  profile: BarberProfile | undefined,
  salonId: string
): BarberProfileSalon | null {
  if (!profile) {
    return null
  }

  return (
    profile.salons.find((salon) => salon.salonId === salonId) ??
    profile.salons.find((salon) => salon.isPrimary) ??
    null
  )
}
