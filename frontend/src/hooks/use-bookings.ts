import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  cancelBooking as cancelBookingRequest,
  fetchBookings,
  type Booking,
} from '@/lib/bookings'

export type CustomerBookingsController = {
  /** Активные брони с `EndAt > now`; включён всегда — от него зависит «Ближайшая запись». */
  upcoming: PagedList<Booking>
  /** История: терминальные и прошедшие; грузится при первом открытии вкладки. */
  history: PagedList<Booking>
  /** Отмена убирает строку из «Предстоящих» по политике пустой страницы. */
  cancelBooking: (bookingId: string) => Promise<void>
}

const LIST_FALLBACK = 'Не удалось загрузить записи.'
const PAGE_FALLBACK = 'Не удалось загрузить ещё записи.'

const getBookingId = (booking: Booking): string => booking.id

/**
 * Два серверных списка броней клиента: «Предстоящие» и «История».
 *
 * Скоуп определяет сервер, поэтому `fetchPage` мемоизирован отдельно для каждого
 * списка: смена вкладки включает историю, и `usePagedList` сам грузит её первую
 * страницу. История ленивая — на входе в кабинет запрашиваются только предстоящие.
 * После отмены оптимистичных правок нет: сначала ответ сервера, потом удаление
 * строки; историю вручную не инвалидируем — она либо ещё не открывалась, либо
 * перезагрузится при следующем включении.
 */
export function useCustomerBookings({
  historyEnabled,
}: {
  historyEnabled: boolean
}): CustomerBookingsController {
  const fetchUpcomingPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchBookings('upcoming', cursor, signal),
    []
  )

  const fetchHistoryPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchBookings('past', cursor, signal),
    []
  )

  const upcoming = usePagedList<Booking>({
    getId: getBookingId,
    fetchPage: fetchUpcomingPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
  })

  const history = usePagedList<Booking>({
    getId: getBookingId,
    fetchPage: fetchHistoryPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
    enabled: historyEnabled,
  })

  // Метод пагинатора стабилен между рендерами, поэтому отмена не пересоздаётся
  // на каждый ответ списка и не тянет за собой лишние запросы.
  const removeUpcomingItem = upcoming.removeItem

  const cancelBooking = useCallback(
    async (bookingId: string) => {
      await cancelBookingRequest(bookingId)
      removeUpcomingItem(bookingId)
    },
    [removeUpcomingItem]
  )

  return { upcoming, history, cancelBooking }
}
