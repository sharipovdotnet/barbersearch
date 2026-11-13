import { useCallback, useEffect, useRef, useState } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import { useI18n } from '@/i18n'
import type { BarberSlot } from '@/lib/barber-slots'
import {
  cancelBooking as cancelBookingRequest,
  fetchBookings,
  rescheduleBooking as rescheduleBookingRequest,
  type Booking,
} from '@/lib/bookings'
import { diffPageItems, type PageChange } from '@/lib/page-changes'

export type CustomerBookingsController = {
  /** Активные брони с `EndAt > now`; включён всегда — от него зависит «Ближайшая запись». */
  upcoming: PagedList<Booking>
  /** История: терминальные и прошедшие; грузится при первом открытии вкладки. */
  history: PagedList<Booking>
  /** Отмена убирает строку из «Предстоящих» по политике пустой страницы. */
  cancelBooking: (bookingId: string) => Promise<void>
  /**
   * Тихое обновление «Предстоящих» с разбором изменений: баннер появляется только
   * на новую запись, подтверждение, перенос или отмену. `null` — запрос не удался.
   */
  refreshUpcoming: () => Promise<PageChange<Booking> | null>
  /** Отмена в полёте: экран на это время останавливает опрос. */
  isCancelling: boolean
  /**
   * Перенос визита: запрос, затем правка строки на всех закэшированных страницах.
   * Порядок не пересортировывается — это сделает ближайший тик опроса.
   */
  reschedule: (bookingId: string, slot: BarberSlot) => Promise<void>
  /** Перенос в полёте: экран на это время останавливает опрос. */
  isRescheduling: boolean
}

const LIST_FALLBACK = 'Не удалось загрузить записи.'
const PAGE_FALLBACK = 'Не удалось загрузить ещё записи.'

const getBookingId = (booking: Booking): string => booking.id

/**
 * Что изменилось в строке: статус или время.
 *
 * Без сравнения `startAt` перенос барбером не попал бы в `updated`, и клиент не
 * увидел бы баннер «Барбер перенёс…».
 */
const bookingChanged = (before: Booking, after: Booking): boolean =>
  before.status !== after.status || before.startAt !== after.startAt

/**
 * Два серверных списка броней клиента: «Предстоящие» и «История».
 *
 * Скоуп определяет сервер, поэтому `fetchPage` мемоизирован отдельно для каждого
 * списка: смена вкладки включает историю, и `usePagedList` сам грузит её первую
 * страницу. История ленивая — на входе в кабинет запрашиваются только предстоящие.
 * После отмены оптимистичных правок нет: сначала ответ сервера, потом удаление
 * строки; историю вручную не инвалидируем — она либо ещё не открывалась, либо
 * перезагрузится при следующем включении.
 *
 * `refreshUpcoming` — обратная операция для опроса: барбер подтверждает или отменяет
 * запись, клиент видит это без перезагрузки. Опрос не трогает «Историю»: это лог
 * прошлых визитов, ему хватает загрузки при открытии вкладки.
 *
 * `userId` передаётся в каждый запрос: кабинет — это «мои записи» для любой роли,
 * а администратор без фильтра получил бы с сервера все брони системы.
 */
export function useCustomerBookings({
  historyEnabled,
  userId,
}: {
  historyEnabled: boolean
  userId: string
}): CustomerBookingsController {
  const { t } = useI18n()
  const fetchUpcomingPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchBookings('upcoming', cursor, signal, userId),
    [userId]
  )

  const fetchHistoryPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchBookings('past', cursor, signal, userId),
    [userId]
  )

  const upcoming = usePagedList<Booking>({
    getId: getBookingId,
    fetchPage: fetchUpcomingPage,
    fallback: t(LIST_FALLBACK),
    pageFallback: t(PAGE_FALLBACK),
    requiresAuth: true,
  })

  const history = usePagedList<Booking>({
    getId: getBookingId,
    fetchPage: fetchHistoryPage,
    fallback: t(LIST_FALLBACK),
    pageFallback: t(PAGE_FALLBACK),
    requiresAuth: true,
    enabled: historyEnabled,
  })

  // Строки «Предстоящих» до тихого обновления — база для «исчезла»/«изменилась».
  const upcomingPageRef = useRef<Booking[]>([])

  useEffect(() => {
    upcomingPageRef.current = upcoming.items
  }, [upcoming.items])

  const refreshUpcomingPage = upcoming.refreshPage

  const refreshUpcoming = useCallback(async (): Promise<PageChange<Booking> | null> => {
    const previousPage = upcomingPageRef.current
    const page = await refreshUpcomingPage()

    if (page === null) {
      return null
    }

    return diffPageItems(previousPage, page.items, getBookingId, bookingChanged)
  }, [refreshUpcomingPage])

  // Пока отмена или перенос в полёте, опрос выключен: ответ, прочитанный до коммита,
  // вернул бы строку обратно.
  const [isCancelling, setIsCancelling] = useState(false)
  const [isRescheduling, setIsRescheduling] = useState(false)

  // Метод пагинатора стабилен между рендерами, поэтому отмена не пересоздаётся
  // на каждый ответ списка и не тянет за собой лишние запросы.
  const removeUpcomingItem = upcoming.removeItem

  const cancelBooking = useCallback(
    async (bookingId: string) => {
      setIsCancelling(true)

      try {
        await cancelBookingRequest(bookingId)
        removeUpcomingItem(bookingId)
      } finally {
        setIsCancelling(false)
      }
    },
    [removeUpcomingItem]
  )

  const replaceUpcomingItem = upcoming.replaceItem

  /**
   * Перенос меняет только время строки и не делает `reload()`.
   *
   * Повторное чтение вернуло бы страницу, собранную до коммита, и следующий дифф
   * объявил бы клиенту его собственное действие «Барбер перенёс…». Строка остаётся
   * на старом месте до ближайшего тика — сервер сам восстановит порядок.
   */
  const reschedule = useCallback(
    async (bookingId: string, slot: BarberSlot) => {
      setIsRescheduling(true)

      try {
        await rescheduleBookingRequest(bookingId, slot.startAt)

        const current = upcoming.items.find((booking) => booking.id === bookingId)

        if (current) {
          replaceUpcomingItem(bookingId, {
            ...current,
            startAt: slot.startAt,
            endAt: slot.endAt,
          })
        }
      } finally {
        setIsRescheduling(false)
      }
    },
    [upcoming.items, replaceUpcomingItem]
  )

  return {
    upcoming,
    history,
    cancelBooking,
    refreshUpcoming,
    isCancelling,
    reschedule,
    isRescheduling,
  }
}
