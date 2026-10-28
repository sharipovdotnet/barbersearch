import { useCallback, useEffect, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminReviewDetailError,
  fetchAdminReview,
  type AdminReview,
} from '@/lib/admin-reviews'
import { isReviewId } from '@/lib/admin-path'

export type AdminReviewDetailStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'error'
  | 'notFound'

export type AdminReviewDetailController = {
  review: AdminReview | null
  status: AdminReviewDetailStatus
  error: string | null
  reload: () => void
}

type DetailState = {
  /** Ключ запроса: id + номер попытки. Позволяет отличить свежий ответ от старого. */
  key: string | null
  review: AdminReview | null
  status: 'ready' | 'error' | 'notFound'
  error: string | null
}

/**
 * Отзыв по прямому URL.
 *
 * Список уже содержит полный DTO, поэтому запрос идёт только когда карточки нет
 * на загруженной странице: так клик по строке не делает лишний round-trip, а ссылка
 * на отзыв вне первой страницы всё равно открывается.
 */
export function useAdminReview(
  reviewId: string | null,
  enabled: boolean
): AdminReviewDetailController {
  const [state, setState] = useState<DetailState>({
    key: null,
    review: null,
    status: 'notFound',
    error: null,
  })
  const [reloadToken, setReloadToken] = useState(0)

  const requestKey = reviewId ? `${reviewId}:${reloadToken}` : null
  const shouldFetch = Boolean(reviewId && enabled && isReviewId(reviewId))

  useEffect(() => {
    if (!reviewId || !shouldFetch || !requestKey) {
      return
    }

    const controller = new AbortController()
    let isActive = true

    fetchAdminReview(reviewId, controller.signal)
      .then((result) => {
        if (!isActive) {
          return
        }

        setState({
          key: requestKey,
          review: result,
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
            review: null,
            status: 'notFound',
            error: null,
          })
          return
        }

        setState({
          key: requestKey,
          review: null,
          status: 'error',
          error:
            cause instanceof ApiError && cause.status === 401
              ? 'Сессия истекла — войдите снова.'
              : describeAdminReviewDetailError(cause),
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [reviewId, shouldFetch, requestKey])

  const isCurrent = requestKey !== null && state.key === requestKey

  let status: AdminReviewDetailStatus = 'idle'

  if (reviewId) {
    if (!isReviewId(reviewId)) {
      status = 'notFound'
    } else if (enabled) {
      status = isCurrent ? state.status : 'loading'
    }
  }

  const reload = useCallback(() => {
    setReloadToken((current) => current + 1)
  }, [])

  return {
    review: isCurrent ? state.review : null,
    status,
    error: isCurrent ? state.error : null,
    reload,
  }
}
