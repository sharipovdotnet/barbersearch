import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  describeAdminCatalogueError,
  fetchAdminHairServices,
  fetchAdminSalons,
  fetchAdminSpecializations,
  type AdminCatalogueStatus,
  type AdminHairService,
  type AdminSalon,
  type AdminSpecialization,
  type CatalogueTab,
} from '@/lib/admin-catalogue'

/** Строки всех трёх справочников: тип определяется вкладкой. */
type CatalogueRows = AdminHairService[] | AdminSpecialization[] | AdminSalon[]

type CatalogueListStatus = 'loading' | 'ready' | 'error'

type CatalogueListState = {
  rows: CatalogueRows
  status: CatalogueListStatus
  error: string | null
  requiresSignIn: boolean
  isRefreshing: boolean
  refreshError: string | null
  updatedAt: number | null
}

type CommonListFields = {
  status: CatalogueListStatus
  error: string | null
  /** `401`: сессия закончилась, экран предложит войти снова. */
  requiresSignIn: boolean
  /** Тихое обновление: строки остаются на экране, скелетона нет. */
  isRefreshing: boolean
  refreshError: string | null
  /** Когда ответ последний раз пришёл с сервера. */
  updatedAt: number | null
  reload: () => void
  refresh: () => void
}

/**
 * Результат хука: вкладка сужает тип строк.
 *
 * Дискриминированный союз, а не один общий тип: таблица услуг не должна принимать салоны,
 * и TypeScript проверяет это без приведения типов на экране.
 */
export type AdminCatalogueList =
  | (CommonListFields & { tab: 'services'; rows: AdminHairService[] })
  | (CommonListFields & { tab: 'specializations'; rows: AdminSpecialization[] })
  | (CommonListFields & { tab: 'salons'; rows: AdminSalon[] })

const LIST_FALLBACK = 'Не удалось загрузить справочник.'

const INITIAL_STATE: CatalogueListState = {
  rows: [],
  status: 'loading',
  error: null,
  requiresSignIn: false,
  isRefreshing: false,
  refreshError: null,
  updatedAt: null,
}

/**
 * Список одного справочника каталога.
 *
 * В отличие от остальных админских списков здесь **нет** `usePagedList`: справочник
 * отдаётся целиком, страниц и курсоров у него нет. Запрос перезапускается сменой
 * `kind`, статуса или поиска; поздние ответы отменённых запросов отсекаются поколением,
 * поэтому StrictMode оставляет один живой запрос.
 */
export function useAdminCatalogueList(
  tab: CatalogueTab,
  {
    status,
    search,
  }: {
    status: AdminCatalogueStatus
    search: string
  }
): AdminCatalogueList {
  const [state, setState] = useState<CatalogueListState>(INITIAL_STATE)

  const fetchRows = useCallback(
    async (signal: AbortSignal): Promise<CatalogueRows> => {
      switch (tab) {
        case 'services':
          return fetchAdminHairServices({ status, search, signal })
        case 'specializations':
          return fetchAdminSpecializations({ status, search, signal })
        case 'salons':
          return fetchAdminSalons({ status, search, signal })
      }
    },
    [tab, status, search]
  )

  // Поколение отсекает ответы отменённых запросов и чужих вкладок.
  const versionRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)

  const load = useCallback(
    (mode: 'initial' | 'refresh') => {
      const version = versionRef.current + 1
      versionRef.current = version

      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller

      setState((previous) =>
        mode === 'initial'
          ? { ...INITIAL_STATE, status: 'loading' }
          : { ...previous, isRefreshing: true, refreshError: null }
      )

      void (async () => {
        try {
          const rows = await fetchRows(controller.signal)

          if (versionRef.current !== version) {
            return
          }

          setState({
            rows,
            status: 'ready',
            error: null,
            requiresSignIn: false,
            isRefreshing: false,
            refreshError: null,
            updatedAt: Date.now(),
          })
        } catch (cause) {
          if (versionRef.current !== version || controller.signal.aborted) {
            return
          }

          const message = describeAdminCatalogueError(cause, LIST_FALLBACK)
          const isUnauthorized = cause instanceof ApiError && cause.status === 401

          // Ошибка тихого обновления не стирает уже показанные строки.
          if (mode === 'refresh' && !isUnauthorized) {
            setState((previous) => ({
              ...previous,
              isRefreshing: false,
              refreshError: message,
            }))
            return
          }

          setState((previous) => ({
            ...previous,
            status: 'error',
            error: message,
            requiresSignIn: isUnauthorized,
            isRefreshing: false,
          }))
        }
      })()
    },
    [fetchRows]
  )

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- справочник — синхронизация с сервером
    load('initial')
  }, [load])

  useEffect(() => () => abortRef.current?.abort(), [])

  const reload = useCallback(() => load('initial'), [load])
  const refresh = useCallback(() => load('refresh'), [load])

  const common: CommonListFields = {
    status: state.status,
    error: state.error,
    requiresSignIn: state.requiresSignIn,
    isRefreshing: state.isRefreshing,
    refreshError: state.refreshError,
    updatedAt: state.updatedAt,
    reload,
    refresh,
  }

  // `rows` и `tab` меняются одним и тем же запросом, поэтому строки всегда принадлежат
  // активной вкладке: сужение типа здесь — единственное место, где это известно только
  // по значению `tab`.
  switch (tab) {
    case 'services':
      return { ...common, tab, rows: state.rows as AdminHairService[] }
    case 'specializations':
      return { ...common, tab, rows: state.rows as AdminSpecialization[] }
    case 'salons':
      return { ...common, tab, rows: state.rows as AdminSalon[] }
  }
}
