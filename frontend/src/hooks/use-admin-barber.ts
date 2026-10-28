import { useCallback, useEffect, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminBarberDetailError,
  fetchAdminBarber,
  type AdminBarberDetail,
} from '@/lib/admin-barbers'
import { isBarberId } from '@/lib/admin-path'

export type AdminBarberDetailStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'error'
  | 'notFound'

export type AdminBarberDetailController = {
  barber: AdminBarberDetail | null
  status: AdminBarberDetailStatus
  error: string | null
  reload: () => void
}

type DetailState = {
  /** Ключ запроса: id + номер попытки. Позволяет отличить свежий ответ от старого. */
  key: string | null
  barber: AdminBarberDetail | null
  status: 'ready' | 'error' | 'notFound'
  error: string | null
}

/**
 * Карточка барбера по адресу.
 *
 * Список содержит только строку очереди — без контактов и чек-листа готовности,
 * поэтому карточка всегда читается отдельным запросом: локальной копии у неё нет.
 */
export function useAdminBarber(
  barberId: string | null
): AdminBarberDetailController {
  const [state, setState] = useState<DetailState>({
    key: null,
    barber: null,
    status: 'notFound',
    error: null,
  })
  const [reloadToken, setReloadToken] = useState(0)

  const requestKey = barberId ? `${barberId}:${reloadToken}` : null
  const shouldFetch = Boolean(barberId && isBarberId(barberId))

  useEffect(() => {
    if (!barberId || !shouldFetch || !requestKey) {
      return
    }

    const controller = new AbortController()
    let isActive = true

    fetchAdminBarber(barberId, controller.signal)
      .then((result) => {
        if (!isActive) {
          return
        }

        setState({
          key: requestKey,
          barber: result,
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
            barber: null,
            status: 'notFound',
            error: null,
          })
          return
        }

        setState({
          key: requestKey,
          barber: null,
          status: 'error',
          error:
            cause instanceof ApiError && cause.status === 401
              ? 'Сессия истекла — войдите снова.'
              : describeAdminBarberDetailError(cause),
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [barberId, shouldFetch, requestKey])

  const isCurrent = requestKey !== null && state.key === requestKey

  // Перечитывание той же карточки (после действия) не должно мигать скелетоном:
  // пока приходит свежий ответ, на экране остаются прежние данные.
  const isSameBarber =
    barberId !== null && state.key?.startsWith(`${barberId}:`) === true

  let status: AdminBarberDetailStatus = 'idle'

  if (barberId) {
    status = isBarberId(barberId)
      ? isCurrent
        ? state.status
        : 'loading'
      : 'notFound'
  }

  const reload = useCallback(() => {
    setReloadToken((current) => current + 1)
  }, [])

  return {
    barber: isCurrent ? state.barber : isSameBarber ? state.barber : null,
    status,
    error: isCurrent ? state.error : null,
    reload,
  }
}
