import { useCallback, useEffect, useRef, useState } from 'react'

import { describeApiFailure } from '@/lib/errors'
import { fetchBarberReviews, type Review } from '@/lib/reviews'

export type BarberReviewsStatus = 'loading' | 'ready' | 'error'

export type BarberReviewsController = {
  /** Карточки текущей страницы: десять штук или меньше на последней. */
  items: Review[]
  status: BarberReviewsStatus
  error: string | null
  /** Номер показанной страницы, с нуля. */
  pageIndex: number
  /** Сколько карточек в предыдущих страницах — для подписи «11–20 из 32». */
  offset: number
  hasPrev: boolean
  hasNext: boolean
  /** Идёт запрос следующей страницы; текущая остаётся на экране. */
  isLoadingPage: boolean
  /** Ошибка следующей страницы: показанные карточки не стираются. */
  pageError: string | null
  retry: () => void
  goPrev: () => void
  goNext: () => void
}

const REVIEWS_FALLBACK = 'Не удалось загрузить отзывы.'
const PAGE_FALLBACK = 'Не удалось загрузить следующие отзывы.'

/**
 * Опубликованные отзывы барбера по десять карточек на страницу.
 *
 * Список не растёт: каждая страница заменяет предыдущую, а уже загруженные
 * страницы остаются в памяти, поэтому «Назад» работает без запроса. Вперёд
 * идём только по курсору ответа — смещений клиент не придумывает. Кэша нет:
 * отзывы меняются после публикации или скрытия администратором.
 *
 * Запросы отменяются по `AbortController`, а поздний ответ проверяется по
 * поколению: StrictMode оставляет один живой запрос и одну запись состояния.
 */
export function useBarberReviews(
  barberProfileId: string
): BarberReviewsController {
  const [pages, setPages] = useState<Record<number, Review[]>>({})
  const [nextCursors, setNextCursors] = useState<Record<number, string | null>>(
    {}
  )
  const [pageIndex, setPageIndex] = useState(0)
  const [status, setStatus] = useState<BarberReviewsStatus>('loading')
  const [error, setError] = useState<string | null>(null)
  const [requestIndex, setRequestIndex] = useState<number | null>(null)
  const [pageError, setPageError] = useState<string | null>(null)

  const versionRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)

  // Первая страница — синхронизация с сервером, а не вывод из props: без
  // `setState` в `load` статус «загружаем» взять неоткуда.
  const load = useCallback(async () => {
    const current = versionRef.current + 1
    versionRef.current = current

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    setPages({})
    setNextCursors({})
    setPageIndex(0)
    setRequestIndex(null)
    setStatus('loading')
    setError(null)
    setPageError(null)

    try {
      const page = await fetchBarberReviews(
        barberProfileId,
        null,
        controller.signal
      )

      if (versionRef.current !== current) {
        return
      }

      setPages({ 0: page.items })
      setNextCursors({ 0: page.nextCursor })
      setStatus('ready')
    } catch (cause) {
      if (versionRef.current !== current || controller.signal.aborted) {
        return
      }

      setError(describeApiFailure(cause, REVIEWS_FALLBACK))
      setStatus('error')
    }
  }, [barberProfileId])

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- первая страница отзывов
    void load()

    return () => {
      // Поздний ответ отменённого запроса уже не пройдёт проверку поколения.
      versionRef.current += 1
      abortRef.current?.abort()
    }
  }, [load])

  // Следующая страница грузится по запросу, а не эффектом на смену номера:
  // ошибка оставляет текущую страницу на экране и не роняет список.
  useEffect(() => {
    if (requestIndex === null) {
      return
    }

    const cursor = nextCursors[requestIndex - 1]
    if (cursor === undefined) {
      return
    }

    const version = versionRef.current
    const controller = new AbortController()
    let isActive = true

    void (async () => {
      try {
        const page = await fetchBarberReviews(
          barberProfileId,
          cursor,
          controller.signal
        )

        if (!isActive || versionRef.current !== version) {
          return
        }

        setPages((previous) => ({ ...previous, [requestIndex]: page.items }))
        setNextCursors((previous) => ({
          ...previous,
          [requestIndex]: page.nextCursor,
        }))
        setPageIndex(requestIndex)
        setRequestIndex(null)
      } catch (cause) {
        if (!isActive || controller.signal.aborted) {
          return
        }

        if (versionRef.current !== version) {
          return
        }

        setPageError(describeApiFailure(cause, PAGE_FALLBACK))
        setRequestIndex(null)
      }
    })()

    return () => {
      isActive = false
      controller.abort()
    }
  }, [barberProfileId, nextCursors, requestIndex])

  const goNext = useCallback(() => {
    if (requestIndex !== null) {
      return
    }

    if (pages[pageIndex + 1] !== undefined) {
      setPageError(null)
      setPageIndex(pageIndex + 1)
      return
    }

    if (nextCursors[pageIndex] == null) {
      return
    }

    setPageError(null)
    setRequestIndex(pageIndex + 1)
  }, [nextCursors, pageIndex, pages, requestIndex])

  const goPrev = useCallback(() => {
    if (pageIndex === 0) {
      return
    }

    // Отменяем недогруженную следующую страницу: иначе её ответ перепрыгнет
    // через назад и вернёт пользователя вперёд.
    setRequestIndex(null)
    setPageError(null)
    setPageIndex(pageIndex - 1)
  }, [pageIndex])

  const items = pages[pageIndex] ?? []
  let offset = 0
  for (let index = 0; index < pageIndex; index += 1) {
    offset += pages[index]?.length ?? 0
  }

  return {
    items,
    status,
    error,
    pageIndex,
    offset,
    hasPrev: pageIndex > 0,
    hasNext: nextCursors[pageIndex] != null,
    isLoadingPage: requestIndex !== null,
    pageError,
    retry: () => {
      void load()
    },
    goPrev,
    goNext,
  }
}
