import { useCallback, useEffect, useRef, useState } from 'react'

import { describeApiFailure } from '@/lib/errors'
import { fetchBarberReviews, type Review } from '@/lib/reviews'

export type BarberReviewsStatus = 'loading' | 'ready' | 'error'

export type BarberReviewsController = {
  items: Review[]
  status: BarberReviewsStatus
  error: string | null
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  retry: () => void
  loadMore: () => void
}

const REVIEWS_FALLBACK = 'Не удалось загрузить отзывы.'
const LOAD_MORE_FALLBACK = 'Не удалось загрузить ещё отзывы.'

/** Дозагрузка не должна дублировать карточки, даже если сервер их повторил. */
function appendUnique(previous: Review[], next: Review[]): Review[] {
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
 * Опубликованные отзывы барбера: первая страница, догрузка по курсору и повтор.
 *
 * Кэша нет: отзывы меняются после публикации или скрытия администратором. Каждый
 * запрос получает свой `AbortController` и поколение — поздний ответ отменённого
 * запроса не пишет состояние, а StrictMode оставляет один живой запрос.
 */
export function useBarberReviews(
  barberProfileId: string
): BarberReviewsController {
  const [items, setItems] = useState<Review[]>([])
  const [status, setStatus] = useState<BarberReviewsStatus>('loading')
  const [error, setError] = useState<string | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)

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
    setNextCursor(null)
    setLoadMoreError(null)
    setIsLoadingMore(false)
    loadingMoreRef.current = false

    try {
      const page = await fetchBarberReviews(
        barberProfileId,
        null,
        controller.signal
      )

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

      setError(describeApiFailure(cause, REVIEWS_FALLBACK))
      setStatus('error')
    }
  }, [barberProfileId])

  // Первая страница — синхронизация с сервером, а не вывод из props: без
  // `setState` в `load` статус «загружаем» взять неоткуда.
  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- первая страница отзывов
    void load()

    return () => {
      // Поздний ответ отменённого запроса уже не пройдёт проверку поколения.
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
      const page = await fetchBarberReviews(barberProfileId, nextCursor)

      if (versionRef.current !== current) {
        return
      }

      setItems((previous) => appendUnique(previous, page.items))
      setNextCursor(page.nextCursor)
    } catch (cause) {
      if (versionRef.current !== current) {
        return
      }

      // Ошибка дозагрузки не стирает уже показанные карточки.
      setLoadMoreError(describeApiFailure(cause, LOAD_MORE_FALLBACK))
    } finally {
      if (versionRef.current === current) {
        loadingMoreRef.current = false
        setIsLoadingMore(false)
      }
    }
  }, [barberProfileId, nextCursor])

  const retry = useCallback(() => {
    void load()
  }, [load])

  return {
    items,
    status,
    error,
    nextCursor,
    isLoadingMore,
    loadMoreError,
    retry,
    loadMore: () => {
      void loadMore()
    },
  }
}
