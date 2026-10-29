import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  fetchBarberSlots,
  isSalonDate,
  type BarberSlots,
} from '@/lib/barber-slots'
import { describeApiFailure } from '@/lib/errors'

export type BarberSlotsStatus = 'loading' | 'ready' | 'error' | 'unavailable'

export type BarberSlotsController = {
  slots: BarberSlots | null
  status: BarberSlotsStatus
  error: string | null
  /** Повтор текущего запроса: имеет смысл только для сети, `429` и `5xx`. */
  retry: () => void
}

const SLOTS_FALLBACK = 'Не удалось загрузить свободное время.'
const SLOTS_NOT_FOUND =
  'Этот салон или услуга больше недоступны. Выберите другой вариант.'
const SLOTS_TIME_ZONE =
  'Расписание салона настроено некорректно. Попробуйте другой салон или загляните позже.'

type SlotsParams = {
  barberProfileId: string
  date: string
  hairServiceId: string | null
  salonId: string
}

type SlotsState = {
  /** Какому набору параметров принадлежит состояние: старые слоты не показываем. */
  key: string
  slots: BarberSlots | null
  status: BarberSlotsStatus
  error: string | null
}

function slotsKey(params: SlotsParams): string {
  return [
    params.barberProfileId,
    params.date,
    params.hairServiceId ?? '',
    params.salonId,
  ].join('|')
}

function initialState(key: string): SlotsState {
  return { key, slots: null, status: 'loading', error: null }
}

/**
 * Свободное время барбера на выбранный день.
 *
 * Кэша нет: слоты меняются от брони и отпуска, поэтому каждая смена даты,
 * услуги или салона — новый запрос. Прошлый запрос отменяется, номер поколения
 * не даёт позднему ответу перезаписать новый, а StrictMode остаётся с одним
 * живым запросом.
 */
export function useBarberSlots(params: SlotsParams): BarberSlotsController {
  const key = slotsKey(params)
  const [state, setState] = useState<SlotsState>(() => initialState(key))
  const [attempts, setAttempts] = useState({ key, count: 0 })
  const versionRef = useRef(0)

  const attempt = attempts.key === key ? attempts.count : 0
  // Смена параметров ещё до эффекта: иначе на долю кадра видны слоты прошлой даты.
  const current = state.key === key ? state : initialState(key)

  const { barberProfileId, date, hairServiceId, salonId } = params

  useEffect(() => {
    // Пустую и битую дату не отправляем: сервер ответит framework-400 без errorCodes.
    if (!isSalonDate(date)) {
      return
    }

    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    fetchBarberSlots(
      { barberProfileId, date, hairServiceId, salonId },
      controller.signal
    )
      .then((slots) => {
        if (versionRef.current !== version) {
          return
        }

        setState({ key, slots, status: 'ready', error: null })
      })
      .catch((cause: unknown) => {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        if (cause instanceof ApiError && cause.status === 404) {
          setState({ key, slots: null, status: 'unavailable', error: SLOTS_NOT_FOUND })
          return
        }

        if (cause instanceof ApiError && cause.status === 409) {
          setState({ key, slots: null, status: 'unavailable', error: SLOTS_TIME_ZONE })
          return
        }

        // 400 в этот срез попасть не должен; если пришёл — это не сетевая ошибка,
        // повторять запрос бессмысленно, а английский `detail` не показываем.
        if (cause instanceof ApiError && cause.status === 400) {
          setState({ key, slots: null, status: 'unavailable', error: SLOTS_NOT_FOUND })
          return
        }

        setState({
          key,
          slots: null,
          status: 'error',
          error: describeApiFailure(cause, SLOTS_FALLBACK),
        })
      })

    return () => {
      // Поздний ответ отменённого запроса уже не пройдёт проверку поколения.
      versionRef.current += 1
      controller.abort()
    }
  }, [barberProfileId, date, hairServiceId, salonId, key, attempt])

  const retry = useCallback(() => {
    setAttempts((previous) => ({
      key,
      count: previous.key === key ? previous.count + 1 : 1,
    }))
    setState((previous) =>
      previous.key === key ? initialState(key) : previous
    )
  }, [key])

  return {
    slots: current.slots,
    status: current.status,
    error: current.error,
    retry,
  }
}
