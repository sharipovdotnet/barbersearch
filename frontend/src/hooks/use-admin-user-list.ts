import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminUserListError,
  fetchAdminUsers,
  type AdminUserRoleFilter,
  type AdminUserRow,
  type AdminUserStatusFilter,
} from '@/lib/admin-users'

export type AdminUserListStatus = 'loading' | 'ready' | 'error'

export type AdminUserListController = {
  items: AdminUserRow[]
  status: AdminUserListStatus
  /** Ошибка первой страницы; список в этот момент пуст. */
  error: string | null
  /** Ошибка тихого обновления: список остаётся на экране. */
  refreshError: string | null
  /** 401 на списке: cookie-сессия закончилась, пока страница была открыта. */
  requiresSignIn: boolean
  nextCursor: string | null
  updatedAt: number | null
  isRefreshing: boolean
  isLoadingMore: boolean
  loadMoreError: string | null
  reload: () => void
  loadMore: () => void
}

/**
 * Список пользователей выбранного статуса и роли с поиском и курсором.
 *
 * Смена фильтра или поисковой строки начинает новую загрузку и отменяет ответы
 * прошлой: иначе медленный ответ на «А» мог бы перезаписать результат по «Анна».
 */
export function useAdminUserList({
  status: statusFilter,
  role,
  search,
}: {
  status: AdminUserStatusFilter
  role: AdminUserRoleFilter
  search: string
}): AdminUserListController {
  const [items, setItems] = useState<AdminUserRow[]>([])
  const [status, setStatus] = useState<AdminUserListStatus>('loading')
  const [error, setError] = useState<string | null>(null)
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<number | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)

  // Каждый новый запрос первой страницы отменяет ответы предыдущих: это важно
  // и для StrictMode, и для быстрой смены фильтра или поиска.
  const version = useRef(0)

  const load = useCallback(
    async (mode: 'initial' | 'refresh', signal?: AbortSignal) => {
      const current = version.current + 1
      version.current = current

      if (mode === 'initial') {
        setStatus('loading')
        setItems([])
        setNextCursor(null)
        setUpdatedAt(null)
        setError(null)
      } else {
        setIsRefreshing(true)
        setRefreshError(null)
      }

      setRequiresSignIn(false)
      setLoadMoreError(null)

      try {
        const page = await fetchAdminUsers({
          status: statusFilter,
          role,
          search: search || undefined,
          signal,
        })

        if (version.current !== current) {
          return
        }

        setItems(page.items)
        setNextCursor(page.nextCursor)
        setUpdatedAt(Date.now())
        setStatus('ready')
        setError(null)
      } catch (cause) {
        if (version.current !== current || signal?.aborted) {
          return
        }

        if (cause instanceof ApiError && cause.status === 401) {
          setRequiresSignIn(true)
          setStatus(mode === 'initial' ? 'error' : 'ready')
          return
        }

        const message = describeAdminUserListError(cause)

        if (mode === 'initial') {
          setError(message)
          setStatus('error')
        } else {
          setRefreshError(message)
        }
      } finally {
        if (version.current === current) {
          setIsRefreshing(false)
        }
      }
    },
    [statusFilter, role, search]
  )

  useEffect(() => {
    const controller = new AbortController()

    // oxlint-disable-next-line react/set-state-in-effect -- загрузка при монтировании и смене фильтра: это синхронизация с сервером
    void load('initial', controller.signal)

    return () => {
      // Инвалидируем ответ, который может прийти после смены фильтра или размонтирования.
      version.current += 1
      controller.abort()
    }
  }, [load])

  const reload = useCallback(() => {
    void load('refresh')
  }, [load])

  const loadMore = useCallback(async () => {
    if (!nextCursor || isLoadingMore) {
      return
    }

    const current = version.current
    setIsLoadingMore(true)
    setLoadMoreError(null)

    try {
      const page = await fetchAdminUsers({
        status: statusFilter,
        role,
        search: search || undefined,
        cursor: nextCursor,
      })

      if (version.current !== current) {
        return
      }

      setItems((previous) => {
        const known = new Set(previous.map((user) => user.id))

        return [
          ...previous,
          ...page.items.filter((user) => !known.has(user.id)),
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

      setLoadMoreError(
        describeAdminUserListError(
          cause,
          'Не удалось загрузить ещё пользователей.'
        )
      )
    } finally {
      if (version.current === current) {
        setIsLoadingMore(false)
      }
    }
  }, [statusFilter, role, search, nextCursor, isLoadingMore])

  return {
    items,
    status,
    error,
    refreshError,
    requiresSignIn,
    nextCursor,
    updatedAt,
    isRefreshing,
    isLoadingMore,
    loadMoreError,
    reload,
    loadMore: () => {
      void loadMore()
    },
  }
}
