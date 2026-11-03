import { useCallback, useEffect, useRef, useState } from 'react'

import { describeApiFailure } from '@/lib/errors'
import {
  fetchBarberSchedule,
  type BarberSchedule,
} from '@/lib/barber-schedule'

export type BarberScheduleStatus = 'loading' | 'ready' | 'error'

type ScheduleState = {
  status: BarberScheduleStatus
  schedule: BarberSchedule | null
  error: string | null
  isRefreshing: boolean
}

const FALLBACK = 'Не удалось загрузить расписание.'

/**
 * Чтение расписания барбера.
 *
 * После мутации экран вызывает `reload()`: сохранять ответ `204` нечем, а сервер
 * нормализует неделю (семь дней) и сам считает отпуска. Пока данные уже есть,
 * повторное чтение не прячет карточки — только помечает их обновляющимися.
 */
export function useBarberSchedule(barberProfileId: string) {
  const [state, setState] = useState<ScheduleState>({
    status: 'loading',
    schedule: null,
    error: null,
    isRefreshing: false,
  })
  const [attempt, setAttempt] = useState(0)
  const versionRef = useRef(0)

  useEffect(() => {
    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    fetchBarberSchedule(barberProfileId, controller.signal)
      .then((schedule) => {
        if (versionRef.current !== version) {
          return
        }

        setState({
          status: 'ready',
          schedule,
          error: null,
          isRefreshing: false,
        })
      })
      .catch((cause: unknown) => {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        const message = describeApiFailure(cause, FALLBACK)

        setState((current) => ({
          status: current.schedule ? 'ready' : 'error',
          schedule: current.schedule,
          error: message,
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
      status: current.schedule ? 'ready' : 'loading',
      error: null,
      isRefreshing: current.schedule !== null,
    }))
    setAttempt((current) => current + 1)
  }, [])

  return {
    ...state,
    reload,
  }
}
