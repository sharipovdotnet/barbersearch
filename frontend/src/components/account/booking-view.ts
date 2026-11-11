import { t } from '@/i18n'
import type { Booking } from '@/lib/bookings'
import {
  findBookingSalon,
  findBookingService,
  type BarberProfile,
} from '@/lib/catalog'

/**
 * Бронь, склеенная с карточкой барбера.
 *
 * Список броней приходит без имён, поэтому строка собирается из двух источников:
 * сама бронь (время, цена, статус) и профиль барбера (мастер, услуга, салон, пояс).
 * Если профиль ещё грузится или недоступен, остаются нейтральные подписи — страница
 * не должна пустеть из-за одного упавшего запроса.
 */
export type BookingView = {
  booking: Booking
  barberName: string
  /** Аватар барбера из уже загруженной карточки; `null`, если фото нет. */
  barberAvatarUrl: string | null
  serviceName: string
  salonName: string
  salonAddress: string
  salonCity: string
  /** Пояс салона: в нём показываются время и дата визита. */
  timeZoneId: string | undefined
  /** Длительность услуги в минутах — из прайса, иначе из границ брони. */
  durationMinutes: number
  /** Карточка барбера уже загружена (а не только запрошена). */
  isResolved: boolean
}

export function buildBookingView(
  booking: Booking,
  profile: BarberProfile | undefined
): BookingView {
  const service = findBookingService(profile, booking.barberServiceId)
  const salon = findBookingSalon(profile, booking.salonId)
  const fallbackDuration = Math.round(
    (Date.parse(booking.endAt) - Date.parse(booking.startAt)) / 60_000
  )

  return {
    booking,
    barberName: profile?.displayName ?? t('Барбер'),
    // BookingResponse намеренно не несёт аватара: источник — карточка барбера.
    barberAvatarUrl: profile?.avatarUrl ?? null,
    serviceName: service?.name ?? t('Услуга'),
    salonName: salon?.name ?? t('Салон'),
    salonAddress: salon?.address ?? '',
    salonCity: salon?.city ?? '',
    timeZoneId: salon?.timeZoneId,
    durationMinutes: service?.durationMinutes ?? fallbackDuration,
    isResolved: Boolean(profile),
  }
}
