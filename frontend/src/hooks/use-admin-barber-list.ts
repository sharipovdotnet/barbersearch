import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  describeAdminBarberListError,
  fetchAdminBarbers,
  type AdminBarber,
  type AdminBarberStatusFilter,
} from '@/lib/admin-barbers'

/** Строка очереди опознаётся по id профиля. */
const getBarberId = (barber: AdminBarber): string => barber.id

/** Ошибка первой страницы; на ней список пуст. */
const LIST_FALLBACK = 'Не удалось загрузить барберов.'
/** Ошибка следующей страницы; текущая остаётся на экране. */
const PAGE_FALLBACK = 'Не удалось загрузить ещё барберов.'

export type AdminBarberListController = PagedList<AdminBarber>

/**
 * Список барберов выбранного фильтра с поиском.
 *
 * Страницы, кэш, отмена запросов и тихое обновление живут в общем `usePagedList`;
 * здесь только сборка запроса. `fetchPage` меняется вместе с фильтром и поиском,
 * поэтому хук сам начинает с первой страницы и отсекает ответ прошлого запроса.
 */
export function useAdminBarberList({
  status,
  search,
}: {
  status: AdminBarberStatusFilter
  search: string
}): AdminBarberListController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchAdminBarbers({
        status,
        search: search || undefined,
        cursor,
        signal,
      }),
    [status, search]
  )

  return usePagedList<AdminBarber>({
    getId: getBarberId,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
    mapError: describeAdminBarberListError,
  })
}
