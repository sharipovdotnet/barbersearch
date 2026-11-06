import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isSalonBarberList } from '@/lib/response-guards'

/**
 * Состав салона в кабинете владельца.
 *
 * Владелец салона (и администратор) читает прикреплённых барберов, ищет кандидатов
 * по имени или email и прикрепляет/открепляет их. Публичная карточка и поиск
 * по-прежнему отдают только активных и подтверждённых барберов; этот экран — рабочий
 * список владельца, поэтому в нём видны и неподтверждённые, и выключенные профили.
 */

export type SalonBarber = {
  barberProfileId: string
  userId: string
  displayName: string
  email: string
  isActive: boolean
  isVerified: boolean
}

/** Минимум символов в поиске: короче — сервер отвечает `400`. */
export const SALON_BARBER_SEARCH_MIN_LENGTH = 3

/** Максимум символов в поиске: длиннее — сервер отвечает `400`. */
export const SALON_BARBER_SEARCH_MAX_LENGTH = 100

export function fetchSalonBarbers(
  salonId: string,
  signal?: AbortSignal
): Promise<SalonBarber[]> {
  return apiRequest<SalonBarber[]>(
    `/salons/${salonId}/barbers`,
    signal ? { signal } : {},
    { validate: isSalonBarberList }
  )
}

export function searchSalonBarberCandidates(
  salonId: string,
  search: string,
  signal?: AbortSignal
): Promise<SalonBarber[]> {
  const query = new URLSearchParams({ search })

  return apiRequest<SalonBarber[]>(
    `/salons/${salonId}/barber-candidates?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isSalonBarberList }
  )
}

/** Прикрепляет профиль к салону; ответ `204`. */
export async function attachSalonBarber(
  salonId: string,
  barberProfileId: string
): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/barbers`, {
    method: 'POST',
    body: JSON.stringify({ barberProfileId }),
  })
}

/** Открепляет профиль от салона; ответ `204`, владельца убрать нельзя (`409`). */
export async function detachSalonBarber(
  salonId: string,
  barberProfileId: string
): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/barbers/${barberProfileId}`, {
    method: 'DELETE',
  })
}

/**
 * Русский текст ошибки состава салона. Публичные коды важнее английского `detail`;
 * `403` и `401` объясняют, почему действие не дошло до домена.
 */
export function describeSalonBarberError(
  error: unknown,
  fallback = 'Не удалось изменить состав салона. Попробуйте ещё раз.'
): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.hasCode('barberProfile.addSalon.duplicate')) {
    return 'Этот барбер уже работает в салоне.'
  }

  if (error.hasCode('barberProfile.notFound')) {
    return 'Барбер не найден — возможно, профиль удалён.'
  }

  if (error.hasCode('salon.barbers.inactive')) {
    return 'Профиль барбера выключен: его нельзя прикрепить к салону.'
  }

  if (error.hasCode('salon.barbers.owner')) {
    return 'Владельца салона нельзя убрать из его салона.'
  }

  if (error.hasCode('barberProfile.removeSalon.notFound')) {
    return 'Этот барбер уже не работает в салоне.'
  }

  if (error.hasCode('salon.notFound') || error.status === 404) {
    return 'Салон больше не доступен.'
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return 'Нет доступа к салону.'
  }

  if (error.status === 401) {
    return 'Сессия истекла — войдите снова.'
  }

  if (error.status === 409) {
    return 'Состав салона изменился. Обновите список и попробуйте снова.'
  }

  return describeApiFailure(error, fallback)
}

/** `409`/`404` значат, что список устарел: диалог перечитывает состав. */
export function isSalonBarberActionStale(error: unknown): boolean {
  return (
    error instanceof ApiError
    && (error.status === 404 || error.status === 409)
  )
}
