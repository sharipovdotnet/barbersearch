import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import {
  deleteMyReview,
  fetchMyReviews,
  updateMyReview,
  type MyReview,
  type UpdateMyReviewInput,
} from '@/lib/my-reviews'

export type MyReviewsStatus = 'idle' | 'loading' | 'ready' | 'error'

export type MyReviewsController = {
  items: MyReview[]
  status: MyReviewsStatus
  /** Текст ошибки первой страницы; `null`, когда всё хорошо. */
  error: string | null
  /** 401 на списке: cookie-сессия кончилась, пока страница была открыта. */
  requiresSignIn: boolean
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  reload: () => void
  loadMore: () => void
  updateReview: (id: string, input: UpdateMyReviewInput) => Promise<void>
  deleteReview: (id: string) => Promise<'deleted' | 'alreadyDeleted'>
}

const LIST_FALLBACK = 'Не удалось загрузить отзывы.'
const LOAD_MORE_FALLBACK = 'Не удалось загрузить ещё отзывы.'

/** Дозагрузка не должна дублировать строки, даже если сервер их повторил. */
function appendUnique(previous: MyReview[], next: MyReview[]): MyReview[] {
  const known = new Set(previous.map((review) => review.id))
  const merged = [...previous]

  for (const review of next) {
    if (!known.has(review.id)) {
      known.add(review.id)
      merged.push(review)
    }
  }

  return merged
}

/**
 * Свои отзывы: первая страница, догрузка по курсору, правка и удаление.
 *
 * Курсор серверный и непрозрачный, сортировка серверная. Правка и удаление меняют
 * только свою строку: повторный `GET` сдвинул бы пагинацию. Кэша и таймеров нет.
 */
export function useMyReviews(): MyReviewsController {
  const [items, setItems] = useState<MyReview[]>([])
  const [status, setStatus] = useState<MyReviewsStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)

  // Поколение отсекает поздние ответы отменённых запросов; StrictMode в разработке
  // иначе запускает первую страницу дважды.
  const versionRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  // Ссылка, а не состояние: двойной клик приходит раньше перерисовки с `isLoadingMore`.
  const loadingMoreRef = useRef(false)

  const load = useCallback(async () => {
    const current = versionRef.current + 1
    versionRef.current = current

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    setItems([])
    setStatus('loading')
    setError(null)
    setRequiresSignIn(false)
    setNextCursor(null)
    setLoadMoreError(null)
    setIsLoadingMore(false)
    loadingMoreRef.current = false

    try {
      const page = await fetchMyReviews(null, controller.signal)

      if (versionRef.current !== current) {
        return
      }

      setItems(page.items)
      setNextCursor(page.nextCursor)
      setStatus('ready')
    } catch (cause) {
      if (versionRef.current !== current || controller.signal.aborted) {
        return
      }

      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        setStatus('error')
        return
      }

      setError(describeApiFailure(cause, LIST_FALLBACK))
      setStatus('error')
    }
  }, [])

  // Первая страница — синхронизация с сервером, а не вывод из props.
  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- первая страница отзывов
    void load()

    return () => {
      versionRef.current += 1
      abortRef.current?.abort()
    }
  }, [load])

  const loadMore = useCallback(async () => {
    if (nextCursor === null || loadingMoreRef.current) {
      return
    }

    const current = versionRef.current
    loadingMoreRef.current = true
    setIsLoadingMore(true)
    setLoadMoreError(null)

    try {
      const page = await fetchMyReviews(nextCursor)

      if (versionRef.current !== current) {
        return
      }

      setItems((previous) => appendUnique(previous, page.items))
      setNextCursor(page.nextCursor)
    } catch (cause) {
      if (versionRef.current !== current) {
        return
      }

      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        return
      }

      // Ошибка дозагрузки не стирает уже показанные строки.
      setLoadMoreError(describeApiFailure(cause, LOAD_MORE_FALLBACK))
    } finally {
      if (versionRef.current === current) {
        loadingMoreRef.current = false
        setIsLoadingMore(false)
      }
    }
  }, [nextCursor])

  // Удаление последней строки страницы при непустом курсоре не должно оставлять
  // ложное «пока нет отзывов»: экран догружает следующую страницу сам после мутации.

  const updateReview = useCallback(
    async (id: string, input: UpdateMyReviewInput) => {
      await updateMyReview(id, input)

      // `updatedAt` сервер не возвращает, а `createdAt` не меняется — строка остаётся
      // на месте и порядок списка не пересортировывается.
      setItems((previous) =>
        previous.map((review) =>
          review.id === id
            ? { ...review, rating: input.rating, text: input.text }
            : review
        )
      )
    },
    []
  )

  const deleteReview = useCallback(
    async (id: string): Promise<'deleted' | 'alreadyDeleted'> => {
      let outcome: 'deleted' | 'alreadyDeleted' = 'deleted'

      try {
        await deleteMyReview(id)
      } catch (cause) {
        // Повторное удаление сервер отдаёт как 404: для UI строка уже удалена.
        if (cause instanceof ApiError && cause.status === 404) {
          outcome = 'alreadyDeleted'
        } else {
          throw cause
        }
      }

      setItems((previous) => previous.filter((review) => review.id !== id))
      return outcome
    },
    []
  )

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
    updateReview,
    deleteReview,
  }
}
