import { useCallback, useEffect, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminSummaryError,
  fetchAdminSummary,
  type AdminSummary,
} from '@/lib/admin-summary'

/** Ошибка первой загрузки: блоков на экране нет, есть «Повторить». */
const LOAD_FALLBACK = 'Не удалось загрузить сводку.'

export type AdminSummaryStatus = 'loading' | 'ready' | 'error'

export type AdminSummaryController = {
  summary: AdminSummary | null
  status: AdminSummaryStatus
  error: string | null
  /** `401` на чтении: экран показывает гейт входа, а не ошибку. */
  requiresSignIn: boolean
  /** Идёт тихое обновление: старые блоки остаются на экране. */
  isRefreshing: boolean
  /** Ошибка обновления: строка над блоками, сами блоки не стираются. */
  refreshError: string | null
  /** Время последнего успешного чтения для «Обновлено в HH:MM». */
  updatedAt: number | null
  reload: () => void
  refresh: () => void
}

type SummaryState = {
  /** Номер запроса, на который пришёл этот ответ: прошлый ответ отбрасывается. */
  token: number
  summary: AdminSummary | null
  status: 'ready' | 'error'
  error: string | null
  requiresSignIn: boolean
}

/** Номер запроса и его вид: первая загрузка (скелетон) или тихое обновление. */
type SummaryRequest = {
  token: number
  mode: 'load' | 'refresh'
}

/**
 * Одно чтение сводки с отменой запроса.
 *
 * `reload` — первая загрузка и «Повторить»: до ответа на экране скелетон.
 * `refresh` — тихое обновление: старые данные остаются, ошибка показывается строкой
 * и не стирает блоки. `401` переводит экран в гейт входа. Ответ прошлого запроса
 * отбрасывается по номеру, поэтому StrictMode и быстрый двойной клик безопасны.
 */
export function useAdminSummary(): AdminSummaryController {
  const [state, setState] = useState<SummaryState>({
    token: -1,
    summary: null,
    status: 'error',
    error: null,
    requiresSignIn: false,
  })
  const [request, setRequest] = useState<SummaryRequest>({
    token: 0,
    mode: 'load',
  })
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<number | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    let isActive = true

    fetchAdminSummary(controller.signal)
      .then((summary) => {
        if (!isActive) {
          return
        }

        setRefreshError(null)
        setUpdatedAt(Date.now())
        setState({
          token: request.token,
          summary,
          status: 'ready',
          error: null,
          requiresSignIn: false,
        })
      })
      .catch((cause: unknown) => {
        if (!isActive || controller.signal.aborted) {
          return
        }

        if (cause instanceof ApiError && cause.status === 401) {
          setState({
            token: request.token,
            summary: null,
            status: 'error',
            error: null,
            requiresSignIn: true,
          })
          return
        }

        const message = describeAdminSummaryError(cause, LOAD_FALLBACK)

        if (request.mode === 'refresh') {
          // Тихое обновление: число на экране было верным на момент чтения,
          // поэтому ошибка становится строкой, а не заменой блоков.
          setRefreshError(message)
          setState((previous) => ({
            token: request.token,
            summary: previous.summary,
            status: previous.summary ? 'ready' : 'error',
            error: previous.summary ? null : message,
            requiresSignIn: false,
          }))
          return
        }

        setState({
          token: request.token,
          summary: null,
          status: 'error',
          error: message,
          requiresSignIn: false,
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [request])

  const reload = useCallback(() => {
    setRefreshError(null)
    setRequest((current) => ({ token: current.token + 1, mode: 'load' }))
  }, [])

  const refresh = useCallback(() => {
    setRefreshError(null)
    setRequest((current) => ({ token: current.token + 1, mode: 'refresh' }))
  }, [])

  const isCurrent = state.token === request.token
  const isRefreshing = request.mode === 'refresh' && !isCurrent

  // Пока идёт тихое обновление, на экране остаются прежние блоки — скелетон
  // показывается только на первой загрузке и после «Повторить».
  const status: AdminSummaryStatus = isCurrent
    ? state.status
    : isRefreshing && state.summary
      ? 'ready'
      : 'loading'

  return {
    summary: isCurrent || isRefreshing ? state.summary : null,
    status,
    error: isCurrent ? state.error : null,
    requiresSignIn: state.requiresSignIn,
    isRefreshing,
    refreshError,
    updatedAt,
    reload,
    refresh,
  }
}
