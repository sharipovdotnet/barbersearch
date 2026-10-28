import { apiRequest } from '@/lib/api'

/**
 * Карточка барбера — единственный источник человекочитаемых данных о брони.
 *
 * В `BookingResponse` лежат только `barberProfileId`, `barberServiceId` и `salonId`,
 * поэтому имя мастера, название услуги, салон и его часовой пояс берутся отсюда.
 * Один запрос на барбера закрывает сразу все его брони, а кэш живёт на уровне модуля:
 * кабинет и карточка барбера делят одни и те же данные.
 */

export type BarberService = {
  barberServiceId: string
  hairServiceId: string
  name: string
  category: string
  defaultDurationMinutes: number
  price: number
  durationMinutes: number
  isActive: boolean
}

export type BarberSalon = {
  id: string
  salonId: string
  name: string
  address: string
  city: string
  country: string
  timeZoneId: string
  isPrimary: boolean
}

export type BarberProfile = {
  id: string
  userId: string
  displayName: string
  grade: string
  bio: string | null
  yearsOfExperience: number
  rating: number | null
  reviewsCount: number
  isActive: boolean
  isVerified: boolean
  services: BarberService[]
  salons: BarberSalon[]
}

const profiles = new Map<string, BarberProfile>()
const inFlight = new Map<string, Promise<BarberProfile>>()

/** Уже загруженная карточка — для синхронной отрисовки до ответа сети. */
export function readCachedBarberProfile(id: string): BarberProfile | null {
  return profiles.get(id) ?? null
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

  const request = apiRequest<BarberProfile>(`/barbers/${id}`)
    .then((profile) => {
      profiles.set(id, profile)
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
): BarberService | null {
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
): BarberSalon | null {
  if (!profile) {
    return null
  }

  return (
    profile.salons.find((salon) => salon.salonId === salonId) ??
    profile.salons.find((salon) => salon.isPrimary) ??
    null
  )
}
