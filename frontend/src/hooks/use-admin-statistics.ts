import { useCallback, useEffect, useState } from 'react'

import { t } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  describeAdminStatisticsError,
  fetchAdminStatistics,
  type AdminStatistics,
  type AdminStatisticsPeriod,
} from '@/lib/admin-statistics'

/** Ошибка первой загрузки: данных на экране нет, есть «Повторить». */
const LOAD_FALLBACK = 'Не удалось загрузить статистику.'

export type AdminStatisticsStatus = 'loading' | 'ready' | 'error'

export type AdminStatisticsController = {
  statistics: AdminStatistics | null
  status: AdminStatisticsStatus
  error: string | null
  /** `401` на чтении: экран показывает гейт входа, а не ошибку. */
  requiresSignIn: boolean
  /** Идёт тихое обновление: прежние секции остаются на экране. */
  isRefreshing: boolean
  /** Ошибка обновления: строка над секциями, данные не стираются. */
  refreshError: string | null
  /** Время последнего успешного чтения для «Обновлено в HH:MM». */
  updatedAt: number | null
  reload: () => void
  refresh: () => void
}

type RequestMode = 'load' | 'refresh'

type StatisticsRequest = {
  /** Номер запроса: ответ прошлого периода отбрасывается. */
  token: number
  mode: RequestMode
  period: AdminStatisticsPeriod
}

type StatisticsState = {
  /** Полный ключ запроса, на который пришёл ответ. */
  key: string
  /** Только период: по нему видно, можно ли оставить старые данные при обновлении. */
  periodKey: string
  statistics: AdminStatistics | null
  status: 'ready' | 'error'
  error: string | null
  requiresSignIn: boolean
}

function periodKey(period: AdminStatisticsPeriod): string {
  return `${period.from}|${period.to}|${period.compare ? '1' : '0'}`
}

function samePeriod(
  left: AdminStatisticsPeriod,
  right: AdminStatisticsPeriod
): boolean {
  return periodKey(left) === periodKey(right)
}

/**
 * Одно чтение статистики за период с отменой запроса.
 *
 * `reload` — первая загрузка и «Повторить»: до ответа на экране скелетон.
 * `refresh` — тихое обновление: прежние секции остаются, ошибка показывается
 * строкой. Смена периода — всегда новая загрузка: данные другого периода
 * нельзя выдавать за текущие. `401` переводит экран в гейт входа.
 *
 * Автообновления нет: числа периода не должны меняться сами под руками.
 * Ответ прошлого запроса отбрасывается по ключу, поэтому StrictMode и быстрый
 * двойной клик безопасны.
 */
export function useAdminStatistics(
  period: AdminStatisticsPeriod
): AdminStatisticsController {
  const [request, setRequest] = useState<StatisticsRequest>({
    token: 0,
    mode: 'load',
    period,
  })
  const [state, setState] = useState<StatisticsState>({
    key: '',
    periodKey: '',
    statistics: null,
    status: 'error',
    error: null,
    requiresSignIn: false,
  })
  const [refreshError, setRefreshError] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<number | null>(null)

  // Смена периода — новый ключ и скелетон. Состояние запроса не трогаем в эффекте:
  // иначе старый период успевал бы стартовать запрос в том же коммите.
  const resolved: StatisticsRequest = samePeriod(request.period, period)
    ? request
    : { token: request.token + 1, mode: 'load', period }

  const currentPeriodKey = periodKey(period)
  const currentKey = `${currentPeriodKey}#${resolved.token}`
  const mode = resolved.mode
  const { from: periodFrom, to: periodTo, compare: periodCompare } = period

  useEffect(() => {
    const controller = new AbortController()
    let isActive = true
    const requestedPeriod = {
      from: periodFrom,
      to: periodTo,
      compare: periodCompare,
    }

    fetchAdminStatistics(requestedPeriod, controller.signal)
      .then((statistics) => {
        if (!isActive) {
          return
        }

        setRefreshError(null)
        setUpdatedAt(Date.now())
        setState({
          key: currentKey,
          periodKey: currentPeriodKey,
          statistics,
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
            key: currentKey,
            periodKey: currentPeriodKey,
            statistics: null,
            status: 'error',
            error: null,
            requiresSignIn: true,
          })
          return
        }

        const message = describeAdminStatisticsError(cause, t(LOAD_FALLBACK))

        if (mode === 'refresh') {
          // Тихое обновление: числа были верны на момент чтения, поэтому ошибка
          // становится строкой, а не заменой всего экрана.
          setRefreshError(message)
          setState((previous) => ({
            key: currentKey,
            periodKey: currentPeriodKey,
            statistics: previous.statistics,
            status: previous.statistics ? 'ready' : 'error',
            error: previous.statistics ? null : message,
            requiresSignIn: false,
          }))
          return
        }

        setState({
          key: currentKey,
          periodKey: currentPeriodKey,
          statistics: null,
          status: 'error',
          error: message,
          requiresSignIn: false,
        })
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [currentKey, currentPeriodKey, mode, periodFrom, periodTo, periodCompare])

  const reload = useCallback(() => {
    setRefreshError(null)
    setRequest((current) => ({ token: current.token + 1, mode: 'load', period }))
  }, [period])

  const refresh = useCallback(() => {
    setRefreshError(null)
    setRequest((current) => ({
      token: current.token + 1,
      mode: 'refresh',
      period,
    }))
  }, [period])

  const isCurrent = state.key === currentKey
  const isRefreshing =
    mode === 'refresh'
    && state.periodKey === currentPeriodKey
    && !isCurrent

  const status: AdminStatisticsStatus = isCurrent
    ? state.status
    : isRefreshing && state.statistics
      ? 'ready'
      : 'loading'

  return {
    statistics:
      isCurrent || isRefreshing ? state.statistics : null,
    status,
    error: isCurrent ? state.error : null,
    requiresSignIn: state.requiresSignIn,
    isRefreshing,
    refreshError,
    updatedAt:
      isCurrent || isRefreshing
        ? updatedAt
        : null,
    reload,
    refresh,
  }
}
