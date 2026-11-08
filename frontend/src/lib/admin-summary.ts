import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import { isAdminSummary } from '@/lib/response-guards'

/**
 * Админская сводка «что требует внимания» (`GET api/admin/summary`).
 *
 * Единственное агрегатное чтение админки: три очереди в одном ответе, без параметров,
 * пагинации и курсора. Счётчики считает сервер теми же предикатами, что фильтры разделов
 * (`is_published = false`, `is_verified = false`, `is_active = false`), поэтому число
 * в сводке — это ровно размер очереди раздела, а «самый старый» — первая строка
 * FIFO-очереди отзывов.
 *
 * Здесь только чтение: мутаций у сводки нет, действия остаются в разделах. Email и телефон
 * клиента в ответ не входят — только имена, поэтому в адресе и в состоянии экрана нет PII.
 */

/** Самая старая строка очереди модерации — компактно, без визита и email. */
export type AdminSummaryOldestReview = {
  id: string
  rating: number
  /** UTC: экран показывает ожидание словами, а не дату и не пояс салона. */
  createdAt: string
  barberDisplayName: string
  customerDisplayName: string
}

export type AdminSummary = {
  reviews: { pendingCount: number; oldest: AdminSummaryOldestReview | null }
  barbers: { unverifiedCount: number }
  salons: { inactiveCount: number }
}

/**
 * Читает сводку.
 *
 * Ни `status`, ни `pageSize`, ни `cursor`: у эндпоинта нет параметров. Тело проверяется
 * строгим guard'ом `isAdminSummary`, поэтому битый ответ становится ошибкой экрана,
 * а не сломанными числами.
 */
export async function fetchAdminSummary(
  signal?: AbortSignal
): Promise<AdminSummary> {
  return apiRequest<AdminSummary>(
    '/admin/summary',
    { signal },
    { validate: isAdminSummary }
  )
}

/**
 * Текст ошибки сводки: `403` — это роль, `429` — частота запросов.
 *
 * Английский `detail` не показывается никогда: неизвестный сбой уходит в запасной
 * русский текст экрана.
 */
export function describeAdminSummaryError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return t(fallback)
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  if (cause.status === 429) {
    return t('Слишком много запросов. Подождите минуту и повторите.')
  }

  return describeApiFailure(cause, fallback)
}
