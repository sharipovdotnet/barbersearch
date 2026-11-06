import { useCallback, useEffect, useRef, useState } from 'react'

import type { BarberSalonSummary } from '@/lib/barber-salons'
import { fetchBarberProfileSalons } from '@/lib/barber-salons'
import { describeApiFailure } from '@/lib/errors'

export type BarberSalonsStatus = 'loading' | 'ready' | 'error'

type SalonsState = {
  status: BarberSalonsStatus
  salons: BarberSalonSummary[]
  error: string | null
  isRefreshing: boolean
}

const FALLBACK = 'Не удалось загрузить салоны.'

/**
 * Чтение привязанных салонов.
 *
 * После привязки экран вызывает `reload()`: ответ `204` сохранять нечем, а порядок
 * (основной первым) и данные карточек задаёт сервер. Пока список уже есть, повторное
 * чтение не прячет карточки — только помечает их обновляющимися.
 */
export function useBarberSalons(barberProfileId: string) {
  const [state, setState] = useState<SalonsState>({
    status: 'loading',
    salons: [],
    error: null,
    isRefreshing: false,
  })
  const [attempt, setAttempt] = useState(0)
  const versionRef = useRef(0)

  useEffect(() => {
    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    fetchBarberProfileSalons(barberProfileId, controller.signal)
      .then((salons) => {
        if (versionRef.current !== version) {
          return
        }

        setState({
          status: 'ready',
          salons,
          error: null,
          isRefreshing: false,
        })
      })
      .catch((cause: unknown) => {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        setState((current) => ({
          status: current.salons.length > 0 ? 'ready' : 'error',
          salons: current.salons,
          error: describeApiFailure(cause, FALLBACK),
          isRefreshing: false,
        }))
      })

    return () => {
      versionRef.current += 1
      controller.abort()
    }
  }, [barberProfileId, attempt])

  const reload = useCallback(() => {
    setState((current) => ({
      ...current,
      status: current.salons.length > 0 ? 'ready' : 'loading',
      error: null,
      isRefreshing: current.salons.length > 0,
    }))
    setAttempt((current) => current + 1)
  }, [])

  return {
    ...state,
    reload,
  }
}
