import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  cancelBooking as cancelBookingRequest,
  fetchBookings,
  type Booking,
} from '@/lib/bookings'
import { describeApiFailure } from '@/lib/errors'

export type BookingsStatus = 'idle' | 'loading' | 'ready' | 'error'

export type BookingsController = {
  items: Booking[]
  status: BookingsStatus
  /** Текст ошибки первой страницы; `null`, когда всё хорошо. */
  error: string | null
  /** 401 на списке: cookie-сессия кончилась, пока страница была открыта. */
  requiresSignIn: boolean
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  reload: () => void
  loadMore: () => void
  cancelBooking: (bookingId: string) => Promise<void>
}

/**
 * Загрузка броней клиента: первая страница, догрузка по курсору и отмена.
 *
 * Курсор серверный и непрозрачный: клиент не пытается считать по нему номера страниц.
 * Отмена обновляет строку на месте — повторный запрос списка сдвинул бы пагинацию.
 */
export function useBookings(): BookingsController {
  const [items, setItems] = useState<Booking[]>([])
  const [status, setStatus] = useState<BookingsStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)

  // Каждый новый запрос первой страницы отменяет ответы предыдущих: иначе StrictMode
  // в разработке и повторное «Повторить» гоняются за одним и тем же состоянием.
  const version = useRef(0)

  const load = useCallback(async () => {
    const current = version.current + 1
    version.current = current

    setStatus('loading')
    setError(null)
    setRequiresSignIn(false)
    setLoadMoreError(null)
    setNextCursor(null)

    try {
      const page = await fetchBookings()

      if (version.current !== current) {
        return
      }

      setItems(page.items)
      setNextCursor(page.nextCursor)
      setStatus('ready')
    } catch (cause) {
      if (version.current !== current) {
        return
      }

      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        setStatus('error')
        return
      }

      setError(describeApiFailure(cause, 'Не удалось загрузить записи.'))
      setStatus('error')
    }
  }, [])

  // Загрузка при монтировании — это синхронизация с сервером, а не вывод из props:
  // стартовый `setState` здесь ставит статус «загружаем», иначе его неоткуда взять.
  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void load()
  }, [load])

  const loadMore = useCallback(async () => {
    if (!nextCursor || isLoadingMore) {
      return
    }

    const current = version.current
    setIsLoadingMore(true)
    setLoadMoreError(null)

    try {
      const page = await fetchBookings(nextCursor)

      if (version.current !== current) {
        return
      }

      setItems((previous) => {
        const known = new Set(previous.map((booking) => booking.id))

        return [
          ...previous,
          ...page.items.filter((booking) => !known.has(booking.id)),
        ]
      })
      setNextCursor(page.nextCursor)
    } catch (cause) {
      if (version.current !== current) {
        return
      }

      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        return
      }

      setLoadMoreError(describeApiFailure(cause, 'Не удалось загрузить ещё записи.'))
    } finally {
      if (version.current === current) {
        setIsLoadingMore(false)
      }
    }
  }, [nextCursor, isLoadingMore])

  const cancelBooking = useCallback(async (bookingId: string) => {
    await cancelBookingRequest(bookingId)

    setItems((previous) =>
      previous.map((booking) =>
        booking.id === bookingId
          ? { ...booking, status: 'Cancelled' as const }
          : booking
      )
    )
  }, [])

  const reload = useCallback(() => {
    void load()
  }, [load])

  return {
    items,
    status,
    error,
    requiresSignIn,
    nextCursor,
    isLoadingMore,
    loadMoreError,
    reload,
    loadMore: () => {
      void loadMore()
    },
    cancelBooking,
  }
}
