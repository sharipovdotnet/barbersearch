import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { AdminBarberRequestFilter } from '@/lib/admin-path'
import type { BarberRequest, BarberRequestStatus } from '@/lib/barber-requests'
import {
  isAdminBarberRequest,
  isAdminBarberRequestPage,
} from '@/lib/response-guards'

/**
 * Админская очередь заявок «Стать барбером» и два решения по ней.
 *
 * Контракт отдельный от `GET /barber-requests/mine`: сервер отдаёт заявителя
 * (имя и email) и того, кто принял решение. Счётчика «всего» нет — только
 * курсорная страница. Ошибки ветвятся по `status` и `errorCodes`, английский
 * `detail` наружу не показывается.
 */

export type { BarberRequestStatus }

/** Заявитель: только имя и email, остальные ПД заявки живут в её полях. */
export type AdminBarberRequestApplicant = {
  id: string
  fullName: string
  email: string
}

export type AdminBarberRequest = BarberRequest & {
  reviewedByUserId: string | null
  applicant: AdminBarberRequestApplicant
}

export type AdminBarberRequestPage = {
  items: AdminBarberRequest[]
  nextCursor: string | null
}

/** Фильтр — часть адреса раздела (`?status=`), поэтому тип живёт в `admin-path`. */
export type { AdminBarberRequestFilter } from '@/lib/admin-path'

/** Размер страницы совпадает с дефолтом сервера. */
export const ADMIN_BARBER_REQUESTS_PAGE_SIZE = 20

/** Максимальная длина причины отказа на сервере; длиннее он ответит `400`. */
export const BARBER_REQUEST_REJECTION_MAX_LENGTH = 500

export const ADMIN_BARBER_REQUEST_FILTERS: readonly AdminBarberRequestFilter[] = [
  'pending',
  'all',
]

/**
 * Подписи фильтров. Очередь — рабочее состояние раздела, «Все» — все заявки
 * (и `Pending`, и разобранные) новыми сверху; счётчиков нет, потому что API не
 * отдаёт `total`.
 */
export const ADMIN_BARBER_REQUEST_FILTER_LABELS: Record<
  AdminBarberRequestFilter,
  string
> = {
  pending: 'Очередь',
  all: 'Все',
}

/** Русские подписи статусов для бейджа и объявлений. */
export const ADMIN_BARBER_REQUEST_STATUS_LABELS: Record<
  BarberRequestStatus,
  string
> = {
  Pending: 'На рассмотрении',
  Approved: 'Одобрена',
  Rejected: 'Отклонена',
}

export async function fetchAdminBarberRequests({
  status,
  cursor,
  signal,
}: {
  status: AdminBarberRequestFilter
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminBarberRequestPage> {
  const query = new URLSearchParams({
    status,
    pageSize: String(ADMIN_BARBER_REQUESTS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminBarberRequestPage>(
    `/admin/barber-requests?${query.toString()}`,
    { signal },
    { validate: isAdminBarberRequestPage }
  )
}

export async function fetchAdminBarberRequest(
  requestId: string,
  signal?: AbortSignal
): Promise<AdminBarberRequest> {
  return apiRequest<AdminBarberRequest>(
    `/admin/barber-requests/${requestId}`,
    { signal },
    { validate: isAdminBarberRequest }
  )
}

/**
 * Одобрение: сервер меняет роль на `Barber` и отзывает все сессии заявителя
 * в одной транзакции, поэтому локально сессию никто не «мягко» обновляет.
 */
export async function approveBarberRequest(
  requestId: string
): Promise<AdminBarberRequest> {
  return mutateWithCsrf<AdminBarberRequest>(
    `/admin/barber-requests/${requestId}/approval`,
    { method: 'POST', validate: isAdminBarberRequest }
  )
}

/** Отказ: причина обязательна, её видит заявитель в своём статусе. */
export async function rejectBarberRequest(
  requestId: string,
  reason: string
): Promise<AdminBarberRequest> {
  return mutateWithCsrf<AdminBarberRequest>(
    `/admin/barber-requests/${requestId}/rejection`,
    {
      method: 'POST',
      body: JSON.stringify({ reason }),
      validate: isAdminBarberRequest,
    }
  )
}

/** Ошибка списка: `403` обрабатывает экран, остальное показывает текст. */
export function describeAdminBarberRequestListError(
  error: unknown,
  fallback = 'Не удалось загрузить заявки.'
): string {
  if (error instanceof ApiError && error.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  return describeApiFailure(error, fallback)
}

/** Ошибка карточки по прямому URL. */
export function describeAdminBarberRequestDetailError(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('barberRequest.notFound')) {
      return 'Заявка не найдена. Возможно, ссылка устарела.'
    }
  }

  return describeApiFailure(error, 'Не удалось загрузить заявку.')
}

/** 401: сессия закончилась, экран предложит войти снова. */
export function isBarberRequestUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}

/** 404 в действии: строку надо убрать из списка, а не показывать ошибку. */
export function isBarberRequestGone(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    (error.status === 404 || error.hasCode('barberRequest.notFound'))
  )
}

export type AdminBarberRequestAction = 'approve' | 'reject'

/**
 * Ошибки решений. `409 barberRequest.review.invalidStatus` значит, что заявку уже
 * рассмотрели в другой вкладке, а `invalidApplicant` — что заявитель перестал быть
 * клиентом; помочь в обоих случаях может только повторное чтение.
 */
export function describeAdminBarberRequestActionError(
  error: unknown,
  action: AdminBarberRequestAction
): string {
  if (error instanceof ApiError) {
    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (error.status === 404 || error.hasCode('barberRequest.notFound')) {
      return 'Заявка больше не существует. Обновите список.'
    }

    if (error.hasCode('barberRequest.review.invalidApplicant')) {
      return 'Роль заявителя уже изменилась — заявку нельзя одобрить автоматически. Обновите список.'
    }

    if (error.hasCode('barberRequest.review.invalidStatus') || error.status === 409) {
      return 'Заявку уже рассмотрели. Обновите список.'
    }

    if (error.hasCode('barberRequest.rejection.reasonRequired')) {
      return 'Укажите причину отказа.'
    }

    if (error.hasCode('barberRequest.rejection.reasonTooLong')) {
      return `Слишком длинная причина — максимум ${BARBER_REQUEST_REJECTION_MAX_LENGTH} символов.`
    }

    if (error.hasCode('auth.csrf.invalid')) {
      return 'Сессия устарела. Обновите страницу и повторите действие.'
    }
  }

  return describeApiFailure(
    error,
    action === 'approve'
      ? 'Не удалось одобрить заявку. Попробуйте ещё раз.'
      : 'Не удалось отклонить заявку. Попробуйте ещё раз.'
  )
}
