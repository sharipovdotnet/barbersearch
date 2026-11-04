import { useCallback, useEffect, useRef, useState } from 'react'

import {
  fetchBarberBookingOptions,
  type BarberBookingOptions,
} from '@/lib/barber-booking-options'
import { describeApiFailure } from '@/lib/errors'

export type BarberBookingOptionsStatus = 'loading' | 'ready' | 'error'

export type BarberBookingOptionsController = {
  options: BarberBookingOptions | null
  status: BarberBookingOptionsStatus
  error: string | null
  retry: () => void
}

type OptionsState = {
  options: BarberBookingOptions | null
  status: BarberBookingOptionsStatus
  error: string | null
}

const INITIAL_STATE: OptionsState = {
  options: null,
  status: 'loading',
  error: null,
}

const FALLBACK = 'Не удалось загрузить услуги и салоны.'

/**
 * Услуги и салоны собственного профиля для формы ручной записи.
 *
 * Один запрос на открытие формы; данных немного, поэтому кэша нет — после правки
 * прайса форма должна показывать актуальные услуги. `AbortController` снимает
 * запрос при закрытии диалога, а номер поколения отбрасывает поздний ответ.
 */
export function useBarberBookingOptions(): BarberBookingOptionsController {
  const [state, setState] = useState<OptionsState>(INITIAL_STATE)
  const [attempt, setAttempt] = useState(0)
  const versionRef = useRef(0)

  useEffect(() => {
    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    fetchBarberBookingOptions(controller.signal)
      .then((options) => {
        if (versionRef.current !== version) {
          return
        }

        setState({ options, status: 'ready', error: null })
      })
      .catch((cause: unknown) => {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        setState({
          options: null,
          status: 'error',
          error: describeApiFailure(cause, FALLBACK),
        })
      })

    return () => {
      versionRef.current += 1
      controller.abort()
    }
  }, [attempt])

  const retry = useCallback(() => {
    setState(INITIAL_STATE)
    setAttempt((previous) => previous + 1)
  }, [])

  return {
    options: state.options,
    status: state.status,
    error: state.error,
    retry,
  }
}
