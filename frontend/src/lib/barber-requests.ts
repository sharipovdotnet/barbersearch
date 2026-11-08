import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { isBarberRequest, isMyBarberRequest } from '@/lib/response-guards'
import { t } from '@/i18n'

/**
 * Заявка клиента на роль барбера: чтение своей заявки и её создание.
 *
 * Публичного списка заявок нет: клиент видит только свою последнюю заявку
 * (`GET /barber-requests/mine`), админская очередь живёт в `admin-barber-requests.ts`.
 * Заявка не принимает роль в теле — `Barber`/`Admin` выдаёт только администратор.
 * Ошибки ветвятся по `status` и `errorCodes`, английский `detail` наружу не попадает.
 */

export type BarberRequestStatus = 'Pending' | 'Approved' | 'Rejected'

export type BarberRequest = {
  id: string
  status: BarberRequestStatus
  displayName: string
  yearsOfExperience: number
  city: string
  about: string | null
  hasSalon: boolean
  createdAt: string
  reviewedAt: string | null
  rejectionReason: string | null
}

/** `request: null` — у пользователя ещё нет заявки. */
export type MyBarberRequestResponse = {
  request: BarberRequest | null
}

/** Тело `POST /api/barber-requests`: роль в нём не передаётся. */
export type BarberRequestInput = {
  displayName: string
  yearsOfExperience: number
  city: string
  about: string | null
  hasSalon: boolean
}

/** Русские подписи статусов: значения из API остаются английскими. */
export const BARBER_REQUEST_STATUS_LABELS: Record<BarberRequestStatus, string> = {
  Pending: 'На рассмотрении',
  Approved: 'Одобрена',
  Rejected: 'Отклонена',
}

/**
 * Границы полей формы совпадают с доменом (`DisplayName` 1..120, стаж 1..70,
 * город 1..100, «о себе» ≤ 2000). Сервер остаётся источником истины, но клиент
 * не должен отправлять заведомо невалидное тело.
 */
export const BARBER_REQUEST_LIMITS = {
  displayName: 120,
  yearsOfExperience: 70,
  city: 100,
  about: 2000,
} as const

export async function fetchMyBarberRequest(
  signal?: AbortSignal
): Promise<MyBarberRequestResponse> {
  return apiRequest<MyBarberRequestResponse>(
    '/barber-requests/mine',
    { signal },
    { validate: isMyBarberRequest }
  )
}

/**
 * Создаёт заявку клиента. `201` возвращает саму заявку, поэтому экран показывает
 * статус без второго чтения.
 */
export async function createBarberRequest(
  input: BarberRequestInput
): Promise<BarberRequest> {
  return mutateWithCsrf<BarberRequest>('/barber-requests', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isBarberRequest,
  })
}

/** Чем закончилась попытка отправить заявку — от этого зависит вид ошибки. */
export type BarberRequestCreateFailure = {
  message: string
  /** `409`: заявка уже ждёт решения, повторять `POST` бессмысленно. */
  isAlreadyPending: boolean
  /** `429` и сеть: имеет смысл предложить «Повторить». */
  canRetry: boolean
  /** `403`: заявку подаёт только клиент. */
  isForbidden: boolean
}

/**
 * Ошибки `POST /api/barber-requests`.
 *
 * `detail` не показываем никогда: он диагностический и английский. Неизвестный
 * `400` (стандартная валидация) получает общий текст с повтором — клиент уже
 * проверил те же границы, поэтому такой ответ означает расхождение контракта.
 */
export function describeBarberRequestCreateFailure(
  error: unknown
): BarberRequestCreateFailure {
  if (!(error instanceof ApiError)) {
    return {
      message: t('Не удалось отправить заявку. Попробуйте ещё раз.'),
      isAlreadyPending: false,
      canRetry: false,
      isForbidden: false,
    }
  }

  if (error.hasCode('barberRequest.create.alreadyPending') || error.status === 409) {
    return {
      message: t(
        'Заявка уже на рассмотрении. Дождитесь решения администратора — статус виден в кабинете.'
      ),
      isAlreadyPending: true,
      canRetry: false,
      isForbidden: false,
    }
  }

  if (error.hasCode('barberRequest.create.forbidden') || error.status === 403) {
    return {
      message: t('Заявку может подать только клиент.'),
      isAlreadyPending: false,
      canRetry: false,
      isForbidden: true,
    }
  }

  if (error.status === 401) {
    return {
      message: t('Сессия истекла — войдите снова.'),
      isAlreadyPending: false,
      canRetry: false,
      isForbidden: false,
    }
  }

  if (error.status === 429) {
    return {
      message: t('Слишком много запросов. Подождите минуту и повторите.'),
      isAlreadyPending: false,
      canRetry: true,
      isForbidden: false,
    }
  }

  if (error.status === 0) {
    return {
      message: describeApiFailure(error, 'Нет связи с сервером.'),
      isAlreadyPending: false,
      canRetry: true,
      isForbidden: false,
    }
  }

  return {
    message: t('Не удалось отправить заявку. Попробуйте ещё раз.'),
    isAlreadyPending: false,
    canRetry: true,
    isForbidden: false,
  }
}

/**
 * Ошибка чтения своей заявки.
 *
 * `401` обрабатывает вызывающий экран (гейт кабинета уже выше), поэтому здесь только
 * транспорт и `429`: карточка в кабинете показывает тихую строку с «Повторить».
 */
export function describeMyBarberRequestError(
  error: unknown,
  fallback = 'Не удалось проверить заявку.'
): string {
  return describeApiFailure(error, fallback)
}

/** 401 на чтении: сессия закончилась, экран-гейт покажет вход. */
export function isBarberRequestUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}
