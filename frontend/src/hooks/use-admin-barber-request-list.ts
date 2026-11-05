import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  describeAdminBarberRequestListError,
  fetchAdminBarberRequests,
  type AdminBarberRequest,
} from '@/lib/admin-barber-requests'
import type { AdminBarberRequestFilter } from '@/lib/admin-path'

/** Строка очереди опознаётся по id заявки. */
const getRequestId = (request: AdminBarberRequest): string => request.id

/** Ошибка первой страницы; на ней список пуст. */
const LIST_FALLBACK = 'Не удалось загрузить заявки.'
/** Ошибка следующей страницы; текущая остаётся на экране. */
const PAGE_FALLBACK = 'Не удалось загрузить ещё заявки.'

export type AdminBarberRequestListController = PagedList<AdminBarberRequest>

/**
 * Очередь и история заявок выбранного фильтра.
 *
 * Страницы, кэш, отмена запросов и тихое обновление живут в общем `usePagedList`;
 * здесь только сборка запроса. `fetchPage` меняется вместе с фильтром, поэтому
 * смена `pending`/`all` сама начинает первую страницу и отсекает ответ прошлого
 * запроса. `401` остаётся гейтом входа, а не общим текстом ошибки.
 */
export function useAdminBarberRequestList(
  status: AdminBarberRequestFilter
): AdminBarberRequestListController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchAdminBarberRequests({ status, cursor, signal }),
    [status]
  )

  return usePagedList<AdminBarberRequest>({
    getId: getRequestId,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
    mapError: describeAdminBarberRequestListError,
  })
}
