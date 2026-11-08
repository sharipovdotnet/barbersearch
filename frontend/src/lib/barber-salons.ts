import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { PlaceKind } from '@/lib/place-kind'
import { isCreatedId, isBarberSalonSummaryList } from '@/lib/response-guards'
import type { SalonInput } from '@/lib/salon-form'
import type { SalonModerationStatus } from '@/lib/salon-moderation'

/**
 * Салоны барбера в кабинете.
 *
 * `GET /api/barber-profiles/{id}/salons` — все привязки профиля, основной салон первым.
 * Список не фильтруется по активности и модерации: «Мои салоны» — рабочий список,
 * а не витрина, поэтому салон «на проверке» и отклонённый тоже видны владельцу.
 * Строка несёт полные поля салона и `ownerUserId`: по нему UI решает, показывать ли
 * правку, а форма заполняет текущие адрес, координаты и контакты без повторного ввода.
 */

export type BarberSalonSummary = {
  salonId: string
  name: string
  city: string
  address: string
  timeZoneId: string
  /** Тип места; от него зависят бейдж и подпись в «Моих салонах». */
  kind: PlaceKind
  /** `false` — скрытое место: в каталог не попадает и не проходит модерацию. */
  isListed: boolean
  isPrimary: boolean
  country: string
  postalCode: string | null
  latitude: number
  longitude: number
  phoneNumber: string | null
  websiteUrl: string | null
  /** Владелец-барбер; `null` у салона, созданного администратором. */
  ownerUserId: string | null
  moderationStatus: SalonModerationStatus
  rejectionReason: string | null
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

export type CreateOwnSalonResult = {
  id: string
}

/**
 * Создание собственного салона барбера: `POST /api/salons/own`.
 *
 * Тело — те же поля, что у админского `POST /api/salons`, поэтому форма общая.
 * Профиль берётся из claim'а, а не из тела: барбер без профиля получит
 * `403 barberProfile.notFound`. Салон создаётся `Pending` и сразу привязывается
 * к профилю, поэтому после `201` достаточно перечитать «Мои салоны».
 */
export async function createOwnSalon(
  input: SalonInput
): Promise<CreateOwnSalonResult> {
  return mutateWithCsrf<CreateOwnSalonResult>('/salons/own', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })
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
    return t(fallback)
  }

  if (error.hasCode('barberProfile.addSalon.duplicate')) {
    return t('Этот салон уже добавлен.')
  }

  if (error.hasCode('barberProfile.addSalon.salonNotFound')) {
    return t('Салон больше не доступен.')
  }

  if (error.status === 403 || error.hasCode('auth.forbidden')) {
    return t('Нет доступа к профилю.')
  }

  if (error.status === 401) {
    return t('Сессия истекла — войдите снова.')
  }

  return describeApiFailure(error, fallback)
}
