import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminReviewListError,
  fetchAdminReviews,
  type AdminReview,
} from '@/lib/admin-reviews'
import type { AdminTab } from '@/lib/admin-path'

export type AdminReviewListStatus = 'loading' | 'ready' | 'error'

export type AdminReviewListController = {
  items: AdminReview[]
  status: AdminReviewListStatus
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
  removeItem: (reviewId: string) => void
}

/**
 * Список отзывов выбранной вкладки: первая страница, догрузка по курсору и тихое обновление.
 *
 * Смена вкладки начинает новую загрузку и отменяет ответы прошлой: иначе опубликованные
 * могли бы перезаписать очередь модерации.
 */
export function useAdminReviewList(tab: AdminTab): AdminReviewListController {
  const [items, setItems] = useState<AdminReview[]>([])
  const [status, setStatus] = useState<AdminReviewListStatus>('loading')
  const [error, setError] = useState<string | null>(null)
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<number | null>(null)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)

  // Каждый новый запрос первой страницы отменяет ответы предыдущих: это важно
  // и для StrictMode, и для быстрого переключения вкладок.
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
        const page = await fetchAdminReviews({ tab, signal })

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

        const message = describeAdminReviewListError(cause)

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
    [tab]
  )

  useEffect(() => {
    const controller = new AbortController()

    // oxlint-disable-next-line react/set-state-in-effect -- загрузка при монтировании и смене вкладки: это синхронизация с сервером
    void load('initial', controller.signal)

    return () => {
      // Инвалидируем ответ, который может прийти после смены вкладки или размонтирования.
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
      const page = await fetchAdminReviews({ tab, cursor: nextCursor })

      if (version.current !== current) {
        return
      }

      setItems((previous) => {
        const known = new Set(previous.map((review) => review.id))

        return [
          ...previous,
          ...page.items.filter((review) => !known.has(review.id)),
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
        describeAdminReviewListError(
          cause,
          'Не удалось загрузить ещё отзывы.'
        )
      )
    } finally {
      if (version.current === current) {
        setIsLoadingMore(false)
      }
    }
  }, [tab, nextCursor, isLoadingMore])

  const removeItem = useCallback((reviewId: string) => {
    setItems((previous) => previous.filter((review) => review.id !== reviewId))
  }, [])

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
    removeItem,
  }
}
