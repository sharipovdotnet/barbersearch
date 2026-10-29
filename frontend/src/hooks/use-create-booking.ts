import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { createBooking, type CreateBookingInput } from '@/lib/bookings'
import { describeApiFailure } from '@/lib/errors'

export type CreateBookingStatus = 'idle' | 'pending' | 'success' | 'error'

/** Что делать после ошибки: повторить с тем же ключом, обновить слоты, войти и т. д. */
export type BookingFailureKind =
  | 'retry'
  | 'refresh'
  | 'signIn'
  | 'fatal'
  | 'unavailable'

export type CreateBookingController = {
  status: CreateBookingStatus
  error: string | null
  failureKind: BookingFailureKind | null
  /** Публичный код ошибки API — экран доразличает им редкие ветки (конфликт ключа). */
  failureCode: string | null
  createdId: string | null
  submit: (input: CreateBookingInput) => void
  reset: () => void
}

type CreateBookingState = {
  status: CreateBookingStatus
  error: string | null
  failureKind: BookingFailureKind | null
  failureCode: string | null
  createdId: string | null
}

type BookingFailure = {
  message: string
  kind: BookingFailureKind
  code: string | null
}

const INITIAL_STATE: CreateBookingState = {
  status: 'idle',
  error: null,
  failureKind: null,
  failureCode: null,
  createdId: null,
}

const FALLBACK = 'Не удалось записаться.'

/**
 * Ответ `POST /api/bookings` → русский текст и следующий шаг.
 *
 * Порядок важен: `401` и `429` проверяются до кодов, редкие `409` — по `errorCodes`,
 * а английский `detail` не показывается никогда (§9.5).
 */
function classifyFailure(error: unknown): BookingFailure {
  if (!(error instanceof ApiError)) {
    return { message: FALLBACK, kind: 'fatal', code: null }
  }

  if (error.status === 401) {
    return { message: 'Сессия истекла — войдите снова.', kind: 'signIn', code: null }
  }

  if (error.status === 409) {
    if (error.hasCode('booking.add.overlap')) {
      return {
        message: 'Это время только что заняли. Выберите другое.',
        kind: 'refresh',
        code: 'booking.add.overlap',
      }
    }

    if (error.hasCode('booking.add.lockTimeout')) {
      return {
        message: 'Не удалось занять время, попробуйте ещё раз.',
        kind: 'retry',
        code: 'booking.add.lockTimeout',
      }
    }

    if (error.hasCode('booking.create.idempotencyKeyConflict')) {
      // Повтор с тем же ключом снова упрётся в конфликт — панель выдаст новый ключ.
      return {
        message: FALLBACK,
        kind: 'fatal',
        code: 'booking.create.idempotencyKeyConflict',
      }
    }

    return { message: FALLBACK, kind: 'retry', code: null }
  }

  if (error.status === 400) {
    if (error.hasCode('booking.create.startAt.inPast')) {
      return {
        message: 'Это время уже прошло. Обновите свободное время.',
        kind: 'refresh',
        code: 'booking.create.startAt.inPast',
      }
    }

    if (error.hasCode('booking.create.startAt.tooFar')) {
      return {
        message: 'Это время слишком далеко. Обновите свободное время.',
        kind: 'refresh',
        code: 'booking.create.startAt.tooFar',
      }
    }

    // period/idempotencyKey/price — повторять ту же попытку бессмысленно.
    return { message: FALLBACK, kind: 'fatal', code: error.codes[0] ?? null }
  }

  if (error.status === 404) {
    return {
      message: 'Эта услуга или салон больше недоступны.',
      kind: 'unavailable',
      code: error.codes[0] ?? null,
    }
  }

  // Сеть, `429` и `5xx`: повтор с тем же ключом безопасен.
  return {
    message: describeApiFailure(error, FALLBACK),
    kind: 'retry',
    code: error.codes[0] ?? null,
  }
}

/**
 * Создание одной брони.
 *
 * Ключ идемпотентности приходит снаружи и живёт в панели записи: тот же ключ должен
 * уйти на все повторы попытки (сеть, `429`, `5xx`, `401`, `409 lockTimeout`), иначе
 * сервер создаст вторую бронь. Повторный `submit` во время запроса игнорируется —
 * это guard от двойного клика.
 */
export function useCreateBooking(): CreateBookingController {
  const [state, setState] = useState<CreateBookingState>(INITIAL_STATE)
  const isPendingRef = useRef(false)
  const generationRef = useRef(0)

  useEffect(() => {
    return () => {
      // После размонтирования ответ не должен трогать состояние.
      generationRef.current += 1
      isPendingRef.current = false
    }
  }, [])

  const submit = useCallback((input: CreateBookingInput) => {
    if (isPendingRef.current) {
      return
    }

    isPendingRef.current = true
    const generation = generationRef.current

    setState({ ...INITIAL_STATE, status: 'pending' })

    createBooking(input)
      .then((createdId) => {
        if (generationRef.current !== generation) {
          return
        }

        isPendingRef.current = false
        setState({ ...INITIAL_STATE, status: 'success', createdId })
      })
      .catch((error: unknown) => {
        if (generationRef.current !== generation) {
          return
        }

        isPendingRef.current = false
        const failure = classifyFailure(error)
        setState({
          status: 'error',
          error: failure.message,
          failureKind: failure.kind,
          failureCode: failure.code,
          createdId: null,
        })
      })
  }, [])

  const reset = useCallback(() => {
    // Сбрасываем и незавершённый запрос: новый ключ начнёт новую попытку.
    generationRef.current += 1
    isPendingRef.current = false
    setState(INITIAL_STATE)
  }, [])

  return {
    status: state.status,
    error: state.error,
    failureKind: state.failureKind,
    failureCode: state.failureCode,
    createdId: state.createdId,
    submit,
    reset,
  }
}
