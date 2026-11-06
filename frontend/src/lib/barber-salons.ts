import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isBarberSalonSummaryList } from '@/lib/response-guards'

/**
 * Салоны барбера в кабинете.
 *
 * `GET /api/barber-profiles/{id}/salons` — все привязки профиля, основной салон первым.
 * Список не фильтруется по активности: после #48 в нём появятся салоны «на проверке»,
 * и клиент не должен их прятать.
 */

export type BarberSalonSummary = {
  salonId: string
  name: string
  city: string
  address: string
  timeZoneId: string
  isPrimary: boolean
}

export type AttachSalonInput = {
  salonId: string
  isPrimary: boolean
}

export function fetchBarberProfileSalons(
  barberProfileId: string,
  signal?: AbortSignal
): Promise<BarberSalonSummary[]> {
  return apiRequest<BarberSalonSummary[]>(
    `/barber-profiles/${barberProfileId}/salons`,
    signal ? { signal } : {},
    { validate: isBarberSalonSummaryList }
  )
}

/**
 * Привязка активного салона к профилю. Контракт `POST` существующий: тело — только
 * `salonId` и `isPrimary`, признак «основной» с прежнего салона снимает домен.
 */
export async function attachSalon(
  barberProfileId: string,
  input: AttachSalonInput
): Promise<void> {
  await mutateWithCsrf<void>(`/barber-profiles/${barberProfileId}/salons`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

/** Выбор был сделан по устаревшему каталогу: список «Мои салоны» пора перечитать. */
export function isSalonAttachStale(error: unknown): boolean {
  return (
    error instanceof ApiError
    && (
      error.hasCode('barberProfile.addSalon.duplicate')
      || error.hasCode('barberProfile.addSalon.salonNotFound')
      || error.status === 404
      || error.status === 409
    )
  )
}

/**
 * Ошибки привязки салона: публичные коды важнее английского `detail`.
 *
 * `409` дубликата и `404` исчезнувшего салона не закрывают диалог: пользователь
 * выбирает другую строку, а список «Мои салоны» обновляется.
 */
export function describeSalonAttachError(
  error: unknown,
  fallback = 'Не удалось добавить салон. Попробуйте ещё раз.'
): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }

  if (error.hasCode('barberProfile.addSalon.duplicate')) {
    return 'Этот салон уже добавлен.'
  }

  if (error.hasCode('barberProfile.addSalon.salonNotFound')) {
    return 'Салон больше не доступен.'
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return 'Нет доступа к профилю.'
  }

  if (error.status === 401) {
    return 'Сессия истекла — войдите снова.'
  }

  return describeApiFailure(error, fallback)
}
