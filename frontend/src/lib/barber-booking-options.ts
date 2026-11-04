import { apiRequest } from '@/lib/api'
import type { BarberProfileSalon, BarberProfileService } from '@/lib/catalog'
import { isBarberBookingOptions } from '@/lib/response-guards'

/**
 * Данные формы «Записать клиента».
 *
 * Собственный профиль доступен и до публикации, поэтому форма не читает публичную
 * карточку (`GET api/barbers/{id}`): та отдаёт только активный и подтверждённый
 * профиль. Ответ повторяет формы услуг и салонов карточки, чтобы UI переиспользовал
 * типы и проверки.
 */

export type BarberBookingOptions = {
  services: BarberProfileService[]
  salons: BarberProfileSalon[]
}

export async function fetchBarberBookingOptions(
  signal?: AbortSignal
): Promise<BarberBookingOptions> {
  return apiRequest<BarberBookingOptions>(
    '/barber/booking-options',
    signal ? { signal } : {},
    { validate: isBarberBookingOptions }
  )
}
