import { useCallback, useEffect, useState } from 'react'

import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  describeAdminBookingsError,
  fetchAdminBooking,
  type AdminBooking,
} from '@/lib/admin-bookings'
import { isBookingId } from '@/lib/admin-path'

export type AdminBookingDetailStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'error'
  | 'notFound'

export type AdminBookingDetailController = {
  booking: AdminBooking | null
  status: AdminBookingDetailStatus
  error: string | null
  reload: () => void
}

type DetailState = {
  /** Ключ запроса: id + номер попытки. Позволяет отличить свежий ответ от старого. */
  key: string | null
  booking: AdminBooking | null
  status: 'ready' | 'error' | 'notFound'
  error: string | null
}

/**
 * Карточка брони по адресу.
 *
 * Список и карточка — разные чтения: строка списка несёт весь контекст, но
 * прямой адрес `/admin/bookings/{id}` должен работать и после перезагрузки,
 * поэтому карточка всегда читается отдельным запросом.
 */
export function useAdminBooking(
  bookingId: string | null
): AdminBookingDetailController {
  const [state, setState] = useState<DetailState>({
    key: null,
    booking: null,
    status: 'notFound',
    error: null,
  })
  const [reloadToken, setReloadToken] = useState(0)

  const requestKey = bookingId ? `${bookingId}:${reloadToken}` : null
  const shouldFetch = Boolean(bookingId && isBookingId(bookingId))

  useEffect(() => {
    if (!bookingId || !shouldFetch || !requestKey) {
      return
    }

    const controller = new AbortController()
    let isActive = true

    fetchAdminBooking(bookingId, controller.signal)
      .then((result) => {
        if (!isActive) {
          return
        }

        setState({
          key: requestKey,
          booking: result,
          status: 'ready',
          error: null,
        })
      })
      .catch((cause: unknown) => {
        if (!isActive || controller.signal.aborted) {
          return
        }

        if (cause instanceof ApiError && cause.status === 404) {
          setState({
            key: requestKey,
            booking: null,
            status: 'notFound',
            error: null,
          })
          return
        }

        setState({
          key: requestKey,
          booking: null,
          status: 'error',
          error: describeAdminBookingsError(
            cause,
            t('Не удалось загрузить бронь.')
          ),
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [bookingId, shouldFetch, requestKey])

  const isCurrent = requestKey !== null && state.key === requestKey

  // Перечитывание той же карточки (после действия) не должно мигать скелетоном:
  // пока приходит свежий ответ, на экране остаются прежние данные.
  const isSameBooking =
    bookingId !== null && state.key?.startsWith(`${bookingId}:`) === true

  let status: AdminBookingDetailStatus = 'idle'

  if (bookingId) {
    status = isBookingId(bookingId)
      ? isCurrent
        ? state.status
        : 'loading'
      : 'notFound'
  }

  const reload = useCallback(() => {
    setReloadToken((current) => current + 1)
  }, [])

  return {
    booking: isCurrent ? state.booking : isSameBooking ? state.booking : null,
    status,
    error: isCurrent ? state.error : null,
    reload,
  }
}
