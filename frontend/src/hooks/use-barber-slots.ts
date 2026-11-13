import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  fetchBarberSlots,
  isSalonDate,
  type BarberSlots,
  type BarberSlotsParams,
} from '@/lib/barber-slots'
import { describeApiFailure } from '@/lib/errors'
import { t } from '@/i18n'

export type BarberSlotsStatus = 'loading' | 'ready' | 'error' | 'unavailable'

export type BarberSlotsController = {
  slots: BarberSlots | null
  status: BarberSlotsStatus
  error: string | null
  /** Повтор текущего запроса: имеет смысл только для сети, `429` и `5xx`. */
  retry: () => void
  /** Перезапрос тех же параметров: после брони или конфликта слоты устарели. */
  reload: () => void
}

const SLOTS_FALLBACK = 'Не удалось загрузить свободное время.'
const SLOTS_NOT_FOUND =
  'Этот салон или услуга больше недоступны. Выберите другой вариант.'
const SLOTS_TIME_ZONE =
  'Расписание салона настроено некорректно. Попробуйте другой салон или загляните позже.'
/** Режим брони: сама запись пропала или её время/место больше недоступны. */
const RESCHEDULE_UNAVAILABLE =
  'Запись больше нельзя перенести: время или место недоступны.'
/** Режим брони: терминальный статус или визит уже начался. */
const RESCHEDULE_STATUS =
  'Запись уже нельзя перенести — обновите страницу.'

type SlotsState = {
  /** Какому набору параметров принадлежит состояние: старые слоты не показываем. */
  key: string
  slots: BarberSlots | null
  status: BarberSlotsStatus
  error: string | null
}

function slotsKey(params: BarberSlotsParams): string {
  if (params.bookingId !== undefined) {
    return ['booking', params.bookingId, params.date].join('|')
  }

  return [
    params.own === true ? 'own' : 'public',
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
 *
 * Режим брони (`bookingId`) читает `GET api/bookings/{id}/reschedule-slots`:
 * сервер берёт длительность из самой брони, поэтому в запросе только дата. Тексты
 * `404`/`400` и `409 booking.reschedule.status` другие: это не «салон исчез», а
 * «перенести уже нельзя».
 */
export function useBarberSlots(params: BarberSlotsParams): BarberSlotsController {
  const key = slotsKey(params)
  const [state, setState] = useState<SlotsState>(() => initialState(key))
  const [attempts, setAttempts] = useState({ key, count: 0 })
  const versionRef = useRef(0)

  const attempt = attempts.key === key ? attempts.count : 0
  // Смена параметров ещё до эффекта: иначе на долю кадра видны слоты прошлой даты.
  const current = state.key === key ? state : initialState(key)

  const isBookingMode = params.bookingId !== undefined
  const bookingId = params.bookingId ?? ''
  const barberProfileId = isBookingMode ? '' : params.barberProfileId
  const hairServiceId = isBookingMode ? null : params.hairServiceId
  const salonId = isBookingMode ? '' : params.salonId
  const own = isBookingMode ? false : (params.own ?? false)
  const { date } = params

  useEffect(() => {
    // Пустую и битую дату не отправляем: сервер ответит framework-400 без errorCodes.
    if (!isSalonDate(date)) {
      return
    }

    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    const request: BarberSlotsParams = isBookingMode
      ? { bookingId, date }
      : { barberProfileId, date, hairServiceId, salonId, own }

    fetchBarberSlots(request, controller.signal)
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

        // Режим брони: 404/400 — запись или её время/место недоступны; 409 —
        // либо терминальный статус (честный текст), либо прежний рассказ про пояс.
        if (isBookingMode) {
          if (cause instanceof ApiError && cause.status === 409) {
            setState({
              key,
              slots: null,
              status: 'unavailable',
              error: cause.hasCode('booking.reschedule.status')
                ? t(RESCHEDULE_STATUS)
                : t(SLOTS_TIME_ZONE),
            })
            return
          }

          if (
            cause instanceof ApiError &&
            (cause.status === 404 || cause.status === 400)
          ) {
            setState({
              key,
              slots: null,
              status: 'unavailable',
              error: t(RESCHEDULE_UNAVAILABLE),
            })
            return
          }
        } else {
          if (cause instanceof ApiError && cause.status === 404) {
            setState({
              key,
              slots: null,
              status: 'unavailable',
              error: t(SLOTS_NOT_FOUND),
            })
            return
          }

          if (cause instanceof ApiError && cause.status === 409) {
            setState({
              key,
              slots: null,
              status: 'unavailable',
              error: t(SLOTS_TIME_ZONE),
            })
            return
          }

          // 400 в этот срез попасть не должен; если пришёл — это не сетевая ошибка,
          // повторять запрос бессмысленно, а английский `detail` не показываем.
          if (cause instanceof ApiError && cause.status === 400) {
            setState({
              key,
              slots: null,
              status: 'unavailable',
              error: t(SLOTS_NOT_FOUND),
            })
            return
          }
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
  }, [bookingId, barberProfileId, date, hairServiceId, salonId, own, isBookingMode, key, attempt])

  // Попытка в том же поколении: дата, услуга и салон не меняются, скелетон — да.
  const reload = useCallback(() => {
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
    retry: reload,
    reload,
  }
}
