import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import type { Page } from '@/lib/paging'

export type PagedListStatus = 'idle' | 'loading' | 'ready' | 'error'

export type PagedListOptions<T> = {
  /** Гейты экрана: пока сессия или роль не готовы, запросов нет. */
  enabled?: boolean
  getId: (item: T) => string
  /**
   * Запрос одной страницы. `cursor: null` — первая страница.
   *
   * Функцию нужно мемоизировать (`useCallback`): её смена — это смена вкладки,
   * фильтра или поиска, и хук сам грузит первую страницу заново.
   */
  fetchPage: (cursor: string | null, signal: AbortSignal) => Promise<Page<T>>
  /** Ошибка первой страницы. */
  fallback: string
  /** Ошибка следующей страницы; по умолчанию — `fallback`. */
  pageFallback?: string
  /** `401` на списке — это гейт входа, а не общий текст ошибки. */
  requiresAuth?: boolean
  /**
   * Перевод ошибки в текст на конкретном экране: `403`, устаревший курсор и
   * прочие публичные коды важнее общего `describeApiFailure`. Функция должна
   * быть стабильной (модульная), иначе она перезапустит загрузку.
   */
  mapError?: (cause: unknown, fallback: string) => string
}

export type PagedList<T> = {
  items: T[]
  status: PagedListStatus
  error: string | null
  requiresSignIn: boolean
  /** Номер показанной страницы, с нуля. */
  pageIndex: number
  /** Сколько строк в предыдущих страницах — для подписи диапазона. */
  offset: number
  hasPrev: boolean
  hasNext: boolean
  /** Идёт запрос следующей страницы; текущая остаётся на экране. */
  isLoadingPage: boolean
  /** Ошибка следующей страницы: показанные строки не стираются. */
  pageError: string | null
  reload: () => void
  /**
   * Тихое обновление текущей страницы: номер страницы, кэш и выбранная
   * карточка остаются на экране. Нужно экранам с кнопкой «Обновить».
   */
  refresh: () => void
  isRefreshing: boolean
  refreshError: string | null
  /** Когда первая страница последний раз пришла с сервера. */
  updatedAt: number | null
  goPrev: () => void
  goNext: () => void
  /** Правка строки — на всех закэшированных страницах. */
  replaceItem: (id: string, next: T) => void
  /** Удаление строки — на всех закэшированных страницах. */
  removeItem: (id: string) => void
}

type PagesState<T> = {
  /** Страницы по номеру; дыр быть не может — грузим только следующую. */
  pages: T[][]
  /** Курсор со страницы N на N+1; `null` — дальше страниц нет. */
  cursors: (string | null)[]
  pageIndex: number
  status: PagedListStatus
  error: string | null
  pageError: string | null
  requiresSignIn: boolean
  isLoadingPage: boolean
  isRefreshing: boolean
  refreshError: string | null
  updatedAt: number | null
}

/**
 * Контроллер курсорного списка: одна страница на экране, «Назад»/«Дальше».
 *
 * Страницы кэшируются в памяти по номеру, поэтому «Назад» не делает запрос.
 * Вперёд идём только по `nextCursor` из ответа — смещений клиент не придумывает;
 * курсор непрозрачен и не разбирается. Поздние ответы отменённых запросов
 * отсекаются поколением, StrictMode оставляет один живой запрос.
 *
 * Ошибка следующей страницы не стирает текущую: она лежит в `pageError` и
 * повторяется тем же `goNext()`. Правка и удаление применяются ко всем
 * закэшированным страницам, иначе «Назад» показал бы старую строку.
 */
export function usePagedList<T>(options: PagedListOptions<T>): PagedList<T> {
  const { enabled = true, fetchPage } = options
  const { getId } = options
  const fallback = options.fallback
  const pageFallback = options.pageFallback ?? options.fallback
  const requiresAuth = options.requiresAuth ?? false
  const mapError = options.mapError

  const [state, setState] = useState<PagesState<T>>(() =>
    createState<T>(enabled)
  )

  // Ссылка на актуальное состояние для действий, вызванных после `await`.
  const stateRef = useRef(state)
  useEffect(() => {
    stateRef.current = state
  }, [state])

  // Поколение отсекает ответы отменённых запросов.
  const versionRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  // Ссылка, а не состояние: двойной клик приходит раньше перерисовки.
  const pendingRef = useRef(false)

  const loadFirst = useCallback(() => {
    const version = versionRef.current + 1
    versionRef.current = version

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    pendingRef.current = false

    setState(createState<T>(true))

    void (async () => {
      try {
        const page = await fetchPage(null, controller.signal)

        if (versionRef.current !== version) {
          return
        }

        setState({
          pages: [page.items],
          cursors: [page.nextCursor],
          pageIndex: 0,
          status: 'ready',
          error: null,
          pageError: null,
          requiresSignIn: false,
          isLoadingPage: false,
          isRefreshing: false,
          refreshError: null,
          updatedAt: Date.now(),
        })
      } catch (cause) {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        if (requiresAuth && isUnauthorized(cause)) {
          setState((previous) => ({
            ...previous,
            status: 'error',
            requiresSignIn: true,
          }))
          return
        }

        setState((previous) => ({
          ...previous,
          status: 'error',
          error: mapFailure(cause, fallback, mapError),
        }))
      }
    })()
  }, [fetchPage, fallback, mapError, requiresAuth])

  useEffect(() => {
    if (!enabled) {
      // Гейт закрыт: запросов нет, а поздний ответ прошлой загрузки уже не пройдёт.
      versionRef.current += 1
      abortRef.current?.abort()
      pendingRef.current = false
      return
    }

    // oxlint-disable-next-line react/set-state-in-effect -- первая страница — синхронизация с сервером
    loadFirst()

    return () => {
      versionRef.current += 1
      abortRef.current?.abort()
    }
  }, [enabled, loadFirst])

  /**
   * Следующая страница грузится по действию, а не эффектом на смену номера:
   * ошибка оставляет текущую страницу на экране.
   */
  const requestPage = useCallback(
    (targetIndex: number, cursor: string) => {
      if (pendingRef.current) {
        return
      }

      pendingRef.current = true
      const version = versionRef.current
      const controller = new AbortController()
      abortRef.current?.abort()
      abortRef.current = controller

      setState((previous) => ({
        ...previous,
        isLoadingPage: true,
        isRefreshing: false,
        pageError: null,
      }))

      void (async () => {
        try {
          const page = await fetchPage(cursor, controller.signal)

          if (versionRef.current !== version) {
            return
          }

          pendingRef.current = false
          setState((previous) => {
            // Повторный ответ уже загруженной страницы ничего не меняет.
            if (previous.pages[targetIndex] !== undefined) {
              return { ...previous, isLoadingPage: false }
            }

            const pages = [...previous.pages]
            const cursors = [...previous.cursors]
            pages[targetIndex] = page.items
            cursors[targetIndex] = page.nextCursor

            return {
              ...previous,
              pages,
              cursors,
              pageIndex: targetIndex,
              isLoadingPage: false,
              pageError: null,
            }
          })
        } catch (cause) {
          if (versionRef.current !== version || controller.signal.aborted) {
            return
          }

          pendingRef.current = false

          if (requiresAuth && isUnauthorized(cause)) {
            setState((previous) => ({
              ...previous,
              isLoadingPage: false,
              requiresSignIn: true,
            }))
            return
          }

          setState((previous) => ({
            ...previous,
            isLoadingPage: false,
            pageError: mapFailure(cause, pageFallback, mapError),
          }))
        }
      })()
    },
    [fetchPage, pageFallback, mapError, requiresAuth]
  )

  const goNext = useCallback(() => {
    if (state.status !== 'ready' || pendingRef.current) {
      return
    }

    const nextIndex = state.pageIndex + 1

    if (state.pages[nextIndex] !== undefined) {
      setState((previous) => ({
        ...previous,
        pageIndex: nextIndex,
        pageError: null,
      }))
      return
    }

    const cursor = state.cursors[state.pageIndex]
    if (cursor === null || cursor === undefined) {
      return
    }

    requestPage(nextIndex, cursor)
  }, [state, requestPage])

  const goPrev = useCallback(() => {
    // На первой странице назад некуда: не трогаем и текущую загрузку.
    if (stateRef.current.pageIndex === 0) {
      return
    }

    // Недогруженная следующая страница не должна прыгнуть вперёд после «Назад»:
    // поколение отсекает ответ, который уже успел прийти к моменту нажатия.
    versionRef.current += 1
    abortRef.current?.abort()
    pendingRef.current = false

    setState((previous) => {
      if (previous.pageIndex === 0) {
        return previous
      }

      return {
        ...previous,
        pageIndex: previous.pageIndex - 1,
        isLoadingPage: false,
        isRefreshing: false,
        pageError: null,
      }
    })
  }, [])

  const reload = useCallback(() => {
    if (!enabled) {
      return
    }

    loadFirst()
  }, [enabled, loadFirst])

  const replaceItem = useCallback(
    (id: string, next: T) => {
      setState((previous) => ({
        ...previous,
        pages: previous.pages.map((pageItems) =>
          pageItems.map((item) => (getId(item) === id ? next : item))
        ),
      }))
    },
    [getId]
  )

  const removeItem = useCallback(
    (id: string) => {
      // Мутация приходит из обработчика, созданного на прошлом рендере, поэтому
      // текущее состояние читаем из ссылки, а не из замыкания.
      const current = stateRef.current

      // Политика пустой страницы: назад, если это не первая страница; иначе
      // первая страница заново по курсору; если курсора нет — пустое состояние.
      const afterRemoval = current.pages.map((pageItems) =>
        pageItems.filter((item) => getId(item) !== id)
      )
      const isEmptied =
        current.status === 'ready' &&
        (afterRemoval[current.pageIndex]?.length ?? 0) === 0 &&
        current.pages[current.pageIndex] !== undefined

      if (isEmptied && current.pageIndex > 0) {
        versionRef.current += 1
        abortRef.current?.abort()
        pendingRef.current = false

        // Опустевшую страницу выбрасываем из кэша и сшиваем курсор предыдущей
        // страницы со следующей: иначе «Дальше» вернёт пустую страницу из памяти.
        const pages = [...afterRemoval]
        const cursors = [...current.cursors]
        pages.splice(current.pageIndex, 1)
        cursors.splice(
          current.pageIndex - 1,
          2,
          current.cursors[current.pageIndex] ?? null
        )

        setState({
          ...current,
          pages,
          cursors,
          pageIndex: current.pageIndex - 1,
          isLoadingPage: false,
          isRefreshing: false,
          pageError: null,
        })
        return
      }

      if (isEmptied && current.cursors[0] != null) {
        loadFirst()
        return
      }

      setState((previous) => ({
        ...previous,
        pages: previous.pages.map((pageItems) =>
          pageItems.filter((item) => getId(item) !== id)
        ),
      }))
    },
    [getId, loadFirst]
  )

  const refresh = useCallback(() => {
    if (!enabled) {
      return
    }

    const targetIndex = stateRef.current.pageIndex
    const targetCursor =
      targetIndex === 0
        ? null
        : (stateRef.current.cursors[targetIndex - 1] ?? null)

    // Курсор текущей страницы потерян (например, страницу уже выбросили из
    // кэша) — честнее начать с первой, чем показать чужие строки.
    if (targetIndex > 0 && targetCursor === null) {
      loadFirst()
      return
    }

    const version = versionRef.current + 1
    versionRef.current = version

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    pendingRef.current = false

    setState((previous) => ({
      ...previous,
      isRefreshing: true,
      isLoadingPage: false,
      refreshError: null,
      requiresSignIn: false,
    }))

    void (async () => {
      try {
        const page = await fetchPage(targetCursor, controller.signal)

        if (versionRef.current !== version) {
          return
        }

        // Пока запрос летел, страницу могли перевернуть: ответ для прошлой
        // страницы уже не актуален, но и флаг обновления не должен зависнуть.
        setState((previous) => {
          if (previous.pageIndex !== targetIndex) {
            return { ...previous, isRefreshing: false }
          }

          const pages = [...previous.pages]
          const cursors = [...previous.cursors]
          pages[targetIndex] = page.items
          cursors[targetIndex] = page.nextCursor

          return {
            ...previous,
            pages,
            cursors,
            status: 'ready',
            error: null,
            pageError: null,
            requiresSignIn: false,
            isLoadingPage: false,
            isRefreshing: false,
            refreshError: null,
            updatedAt: Date.now(),
          }
        })
      } catch (cause) {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        if (requiresAuth && isUnauthorized(cause)) {
          setState((previous) => ({
            ...previous,
            isRefreshing: false,
            requiresSignIn: true,
          }))
          return
        }

        setState((previous) => ({
          ...previous,
          isRefreshing: false,
          refreshError: mapFailure(cause, fallback, mapError),
        }))
      }
    })()
  }, [enabled, fetchPage, fallback, loadFirst, mapError, requiresAuth])

  const currentItems = state.pages[state.pageIndex] ?? []
  let offset = 0
  for (let index = 0; index < state.pageIndex; index += 1) {
    offset += state.pages[index]?.length ?? 0
  }

  return {
    items: enabled ? currentItems : [],
    status: enabled ? state.status : 'idle',
    error: enabled ? state.error : null,
    requiresSignIn: enabled && state.requiresSignIn,
    pageIndex: state.pageIndex,
    offset,
    hasPrev: enabled && state.pageIndex > 0,
    hasNext: enabled && (state.cursors[state.pageIndex] ?? null) !== null,
    isLoadingPage: enabled && state.isLoadingPage,
    pageError: enabled ? state.pageError : null,
    reload,
    refresh,
    isRefreshing: enabled && state.isRefreshing,
    refreshError: enabled ? state.refreshError : null,
    updatedAt: state.updatedAt,
    goPrev,
    goNext,
    replaceItem,
    removeItem,
  }
}

function createState<T>(enabled: boolean): PagesState<T> {
  return {
    pages: [],
    cursors: [],
    pageIndex: 0,
    status: enabled ? 'loading' : 'idle',
    error: null,
    pageError: null,
    requiresSignIn: false,
    isLoadingPage: false,
    isRefreshing: false,
    refreshError: null,
    updatedAt: null,
  }
}

function isUnauthorized(cause: unknown): boolean {
  return cause instanceof ApiError && cause.status === 401
}

/** Экран может уточнить текст ошибки (403, устаревший курсор), иначе — общий. */
function mapFailure(
  cause: unknown,
  fallback: string,
  mapError: ((cause: unknown, fallback: string) => string) | undefined
): string {
  return mapError === undefined
    ? describeApiFailure(cause, fallback)
    : mapError(cause, fallback)
}
