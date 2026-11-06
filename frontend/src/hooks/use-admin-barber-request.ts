import { useCallback, useEffect, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminBarberRequestDetailError,
  fetchAdminBarberRequest,
  type AdminBarberRequest,
} from '@/lib/admin-barber-requests'
import { isBarberRequestId } from '@/lib/admin-path'

export type AdminBarberRequestDetailStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'error'
  | 'notFound'

export type AdminBarberRequestDetailController = {
  request: AdminBarberRequest | null
  status: AdminBarberRequestDetailStatus
  error: string | null
  reload: () => void
}

type DetailState = {
  /** Ключ запроса: id + номер попытки. Позволяет отличить свежий ответ от старого. */
  key: string | null
  request: AdminBarberRequest | null
  status: 'ready' | 'error' | 'notFound'
  error: string | null
}

/**
 * Карточка заявки по адресу.
 *
 * Строка списка не несёт «о себе» и автора решения, поэтому карточка всегда
 * читается отдельным запросом: локальной копии у неё нет. Пока приходит свежий
 * ответ после действия, на экране остаются прежние данные — скелетон не мигает.
 */
export function useAdminBarberRequest(
  requestId: string | null
): AdminBarberRequestDetailController {
  const [state, setState] = useState<DetailState>({
    key: null,
    request: null,
    status: 'notFound',
    error: null,
  })
  const [reloadToken, setReloadToken] = useState(0)

  const requestKey = requestId ? `${requestId}:${reloadToken}` : null
  const shouldFetch = Boolean(requestId && isBarberRequestId(requestId))

  useEffect(() => {
    if (!requestId || !shouldFetch || !requestKey) {
      return
    }

    const controller = new AbortController()
    let isActive = true

    fetchAdminBarberRequest(requestId, controller.signal)
      .then((result) => {
        if (!isActive) {
          return
        }

        setState({
          key: requestKey,
          request: result,
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
            request: null,
            status: 'notFound',
            error: null,
          })
          return
        }

        setState({
          key: requestKey,
          request: null,
          status: 'error',
          error:
            cause instanceof ApiError && cause.status === 401
              ? 'Сессия истекла — войдите снова.'
              : describeAdminBarberRequestDetailError(cause),
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [requestId, shouldFetch, requestKey])

  const isCurrent = requestKey !== null && state.key === requestKey

  const isSameRequest =
    requestId !== null && state.key?.startsWith(`${requestId}:`) === true

  let status: AdminBarberRequestDetailStatus = 'idle'

  if (requestId) {
    status = isBarberRequestId(requestId)
      ? isCurrent
        ? state.status
        : 'loading'
      : 'notFound'
  }

  const reload = useCallback(() => {
    setReloadToken((current) => current + 1)
  }, [])

  return {
    request: isCurrent ? state.request : isSameRequest ? state.request : null,
    status,
    error: isCurrent ? state.error : null,
    reload,
  }
}
