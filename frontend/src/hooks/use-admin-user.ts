import { useCallback, useEffect, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminUserDetailError,
  fetchAdminUser,
  type AdminUserDetail,
} from '@/lib/admin-users'
import { isUserId } from '@/lib/admin-path'

export type AdminUserDetailStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'error'
  | 'notFound'

export type AdminUserDetailController = {
  user: AdminUserDetail | null
  status: AdminUserDetailStatus
  error: string | null
  reload: () => void
}

type DetailState = {
  /** Ключ запроса: id + номер попытки. Позволяет отличить свежий ответ от старого. */
  key: string | null
  user: AdminUserDetail | null
  status: 'ready' | 'error' | 'notFound'
  error: string | null
}

/**
 * Карточка пользователя по адресу.
 *
 * Строка списка не содержит телефона, метаданных входа и профиля барбера,
 * поэтому карточка всегда читается отдельным запросом: локальной копии у неё нет.
 */
export function useAdminUser(
  userId: string | null
): AdminUserDetailController {
  const [state, setState] = useState<DetailState>({
    key: null,
    user: null,
    status: 'notFound',
    error: null,
  })
  const [reloadToken, setReloadToken] = useState(0)

  const requestKey = userId ? `${userId}:${reloadToken}` : null
  const shouldFetch = Boolean(userId && isUserId(userId))

  useEffect(() => {
    if (!userId || !shouldFetch || !requestKey) {
      return
    }

    const controller = new AbortController()
    let isActive = true

    fetchAdminUser(userId, controller.signal)
      .then((result) => {
        if (!isActive) {
          return
        }

        setState({
          key: requestKey,
          user: result,
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
            user: null,
            status: 'notFound',
            error: null,
          })
          return
        }

        setState({
          key: requestKey,
          user: null,
          status: 'error',
          error:
            cause instanceof ApiError && cause.status === 401
              ? 'Сессия истекла — войдите снова.'
              : describeAdminUserDetailError(cause),
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [userId, shouldFetch, requestKey])

  const isCurrent = requestKey !== null && state.key === requestKey

  // Перечитывание той же карточки (после действия) не должно мигать скелетоном:
  // пока приходит свежий ответ, на экране остаются прежние данные.
  const isSameUser =
    userId !== null && state.key?.startsWith(`${userId}:`) === true

  let status: AdminUserDetailStatus = 'idle'

  if (userId) {
    status = isUserId(userId)
      ? isCurrent
        ? state.status
        : 'loading'
      : 'notFound'
  }

  const reload = useCallback(() => {
    setReloadToken((current) => current + 1)
  }, [])

  return {
    user: isCurrent ? state.user : isSameUser ? state.user : null,
    status,
    error: isCurrent ? state.error : null,
    reload,
  }
}
