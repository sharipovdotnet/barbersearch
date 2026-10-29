import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  searchBarbers,
  type BarberSearchFilters,
  type BarberSearchItem,
} from '@/lib/barber-search'
import { describeApiFailure } from '@/lib/errors'

export type BarberSearchStatus = 'loading' | 'ready' | 'error'

export type BarberSearchController = {
  items: BarberSearchItem[]
  status: BarberSearchStatus
  /** Ошибка первой страницы; при ней список пуст. */
  error: string | null
  /** Ошибка дозагрузки: показанный список остаётся на экране. */
  loadMoreError: string | null
  hasMore: boolean
  isLoadingMore: boolean
  submit: (filters: BarberSearchFilters) => void
  loadMore: () => void
  /** Повторяет последний неудачный запрос — с курсором, если падала дозагрузка. */
  retry: () => void
}

type LoadMode = 'replace' | 'append'

const INITIAL_FILTERS: BarberSearchFilters = { city: '', date: '' }

const SEARCH_FALLBACK =
  'Не удалось загрузить барберов. Обновите страницу и попробуйте снова.'

function describeSearchFailure(cause: unknown): string {
  // 400 — битый курсор или параметры, то есть ошибка клиента. Английский `detail`
  // наружу не показываем и предлагаем общий безопасный текст.
  if (cause instanceof ApiError && cause.status === 400) {
    return SEARCH_FALLBACK
  }

  return describeApiFailure(cause, SEARCH_FALLBACK)
}

/** Дозагрузка не должна дублировать карточки, даже если сервер их повторил. */
function appendUnique(
  previous: BarberSearchItem[],
  next: BarberSearchItem[]
): BarberSearchItem[] {
  const seen = new Set(previous.map((item) => item.id))
  const merged = [...previous]

  for (const item of next) {
    if (!seen.has(item.id)) {
      seen.add(item.id)
      merged.push(item)
    }
  }

  return merged
}

/**
 * Состояние поиска барберов на витрине.
 *
 * Каждый запрос получает свой `AbortController` и номер поколения: ответ прошлого
 * поиска не может перезаписать новый список. Дозагрузка живёт отдельно от первой
 * страницы — её ошибка не стирает уже показанные карточки.
 */
export function useBarberSearch(): BarberSearchController {
  const [items, setItems] = useState<BarberSearchItem[]>([])
  const [status, setStatus] = useState<BarberSearchStatus>('loading')
  const [error, setError] = useState<string | null>(null)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [isLoadingMore, setIsLoadingMore] = useState(false)

  const versionRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  const filtersRef = useRef<BarberSearchFilters>(INITIAL_FILTERS)
  const lastFailedRef = useRef<{
    filters: BarberSearchFilters
    cursor: string | null
  } | null>(null)
  const loadingMoreRef = useRef(false)

  const run = useCallback(
    async (
      filters: BarberSearchFilters,
      cursor: string | null,
      mode: LoadMode
    ) => {
      const current = versionRef.current + 1
      versionRef.current = current

      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller

      if (mode === 'replace') {
        // Список очищается сразу: старые карточки под новым фильтром — ложь.
        setItems([])
        setNextCursor(null)
        setStatus('loading')
        setError(null)
        setLoadMoreError(null)
        loadingMoreRef.current = false
        setIsLoadingMore(false)
      } else {
        loadingMoreRef.current = true
        setIsLoadingMore(true)
        setLoadMoreError(null)
      }

      try {
        const page = await searchBarbers(filters, cursor, controller.signal)

        if (versionRef.current !== current) {
          return
        }

        setItems((previous) =>
          mode === 'replace' ? page.items : appendUnique(previous, page.items)
        )
        setNextCursor(page.nextCursor)
        setStatus('ready')
        setError(null)
        lastFailedRef.current = null
      } catch (cause) {
        if (versionRef.current !== current || controller.signal.aborted) {
          return
        }

        const message = describeSearchFailure(cause)
        lastFailedRef.current = { filters, cursor }

        if (mode === 'replace') {
          setError(message)
          setStatus('error')
        } else {
          setLoadMoreError(message)
        }
      } finally {
        if (versionRef.current === current) {
          loadingMoreRef.current = false
          setIsLoadingMore(false)
        }
      }
    },
    []
  )

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- первая загрузка без фильтров: синхронизация с сервером
    void run(INITIAL_FILTERS, null, 'replace')

    return () => {
      // Инвалидируем ответ и отменяем запрос: в StrictMode эффект монтируется дважды.
      versionRef.current += 1
      abortRef.current?.abort()
    }
  }, [run])

  const submit = useCallback(
    (filters: BarberSearchFilters) => {
      const next: BarberSearchFilters = {
        city: filters.city.trim(),
        date: filters.date,
      }

      filtersRef.current = next
      lastFailedRef.current = null
      void run(next, null, 'replace')
    },
    [run]
  )

  const loadMore = useCallback(() => {
    if (loadingMoreRef.current || nextCursor === null) {
      return
    }

    void run(filtersRef.current, nextCursor, 'append')
  }, [nextCursor, run])

  const retry = useCallback(() => {
    const failed = lastFailedRef.current

    if (failed === null) {
      void run(filtersRef.current, null, 'replace')
      return
    }

    void run(
      failed.filters,
      failed.cursor,
      failed.cursor === null ? 'replace' : 'append'
    )
  }, [run])

  return {
    items,
    status,
    error,
    loadMoreError,
    hasMore: status === 'ready' && nextCursor !== null,
    isLoadingMore,
    submit,
    loadMore,
    retry,
  }
}
