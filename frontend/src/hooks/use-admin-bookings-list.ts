import { useCallback } from 'react'

import { t } from '@/i18n'
import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  describeAdminBookingsError,
  fetchAdminBookings,
  type AdminBooking,
  type AdminBookingStatusFilter,
} from '@/lib/admin-bookings'

/** Строка списка опознаётся по id брони. */
const getBookingId = (booking: AdminBooking): string => booking.id

/** Ошибка первой страницы; на ней список пуст. */
const LIST_FALLBACK = 'Не удалось загрузить брони.'
/** Ошибка следующей страницы; текущая остаётся на экране. */
const PAGE_FALLBACK = 'Не удалось загрузить следующую страницу броней.'

export type AdminBookingsListController = PagedList<AdminBooking>

/**
 * Список броней выбранного статуса и поиска.
 *
 * Страницы, кэш, отмена запросов и тихое обновление живут в общем `usePagedList`;
 * здесь только сборка запроса. `fetchPage` меняется вместе с фильтрами, поэтому
 * хук сам начинает с первой страницы и отсекает ответ прошлого запроса.
 * Фильтры в URL не пишутся: email клиента — PII.
 */
export function useAdminBookingsList({
  status,
  search,
}: {
  status: AdminBookingStatusFilter
  search: string
}): AdminBookingsListController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchAdminBookings({
        status,
        search: search || undefined,
        cursor,
        signal,
      }),
    [status, search]
  )

  return usePagedList<AdminBooking>({
    getId: getBookingId,
    fetchPage,
    fallback: t(LIST_FALLBACK),
    pageFallback: t(PAGE_FALLBACK),
    requiresAuth: true,
    mapError: describeAdminBookingsError,
  })
}
