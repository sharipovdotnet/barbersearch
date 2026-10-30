import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import {
  cancelBooking as cancelBookingRequest,
  completeBooking as completeBookingRequest,
  confirmBooking as confirmBookingRequest,
  fetchBarberBookings,
  markBookingNoShow,
  saveBarberNote,
  type BarberBooking,
  type BarberBookingScope,
} from '@/lib/barber-bookings'
import { describeApiFailure } from '@/lib/errors'

export type BarberBookingsStatus = 'idle' | 'loading' | 'ready' | 'error'

export type BarberBookingsController = {
  items: BarberBooking[]
  status: BarberBookingsStatus
  /** Текст ошибки первой страницы; `null`, когда всё хорошо. */
  error: string | null
  /** 401 на списке: cookie-сессия кончилась, пока страница была открыта. */
  requiresSignIn: boolean
  nextCursor: string | null
  isLoadingMore: boolean
  loadMoreError: string | null
  reload: () => void
  loadMore: () => void
  confirm: (id: string) => Promise<void>
  complete: (id: string) => Promise<void>
  markNoShow: (id: string) => Promise<void>
  cancel: (id: string) => Promise<void>
  saveNote: (id: string, note: string) => Promise<void>
}

const LIST_FALLBACK = 'Не удалось загрузить записи.'
const LOAD_MORE_FALLBACK = 'Не удалось загрузить ещё записи.'

/** Дозагрузка не должна дублировать строки, даже если сервер их повторил. */
function appendUnique(
  previous: BarberBooking[],
  next: BarberBooking[]
): BarberBooking[] {
  const known = new Set(previous.map((booking) => booking.id))
  const merged = [...previous]

  for (const booking of next) {
    if (!known.has(booking.id)) {
      known.add(booking.id)
      merged.push(booking)
    }
  }

  return merged
}

/** Терминальный статус уводит строку из «Ближайших»: история живёт в другом скоупе. */
function isTerminal(status: BarberBooking['status']): boolean {
  return status === 'Cancelled' || status === 'Completed' || status === 'NoShow'
}

/**
 * Новый статус строки: в «Ближайших» терминальная строка исчезает, в «Истории»
 * остаётся на месте — она и так принадлежит этому скоупу.
 */
function applyStatus(
  items: BarberBooking[],
  id: string,
  next: BarberBooking['status'],
  scope: BarberBookingScope
): BarberBooking[] {
  if (scope === 'upcoming' && isTerminal(next)) {
    return items.filter((booking) => booking.id !== id)
  }

  return items.map((booking) =>
    booking.id === id ? { ...booking, status: next } : booking
  )
}

/**
 * Записи клиентов барбера: страница, курсор и пять локальных мутаций.
 *
 * После мутации список меняется на месте, без повторного `GET`: новый запрос сдвинул
 * бы курсор и вернул бы уже виденные строки. `409`/`404` пробрасываются в строку или
 * диалог — остальные строки при этом не трогаются.
 */
export function useBarberBookings(
  scope: BarberBookingScope
): BarberBookingsController {
  const [items, setItems] = useState<BarberBooking[]>([])
  const [status, setStatus] = useState<BarberBookingsStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [isLoadingMore, setIsLoadingMore] = useState(false)
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null)

  // Поколение отсекает поздние ответы отменённых запросов; StrictMode в разработке
  // иначе запускает первую страницу дважды.
  const versionRef = useRef(0)
  const abortRef = useRef<AbortController | null>(null)
  // Ссылка, а не состояние: двойной клик приходит раньше перерисовки с `isLoadingMore`.
  const loadingMoreRef = useRef(false)
  // id с запросом в полёте: второй клик по той же строке молча игнорируется.
  const pendingMutationsRef = useRef(new Set<string>())

  const load = useCallback(async () => {
    const current = versionRef.current + 1
    versionRef.current = current

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    setItems([])
    setStatus('loading')
    setError(null)
    setRequiresSignIn(false)
    setNextCursor(null)
    setLoadMoreError(null)
    setIsLoadingMore(false)
    loadingMoreRef.current = false

    try {
      const page = await fetchBarberBookings(scope, null, controller.signal)

      if (versionRef.current !== current) {
        return
      }

      setItems(page.items)
      setNextCursor(page.nextCursor)
      setStatus('ready')
    } catch (cause) {
      if (versionRef.current !== current || controller.signal.aborted) {
        return
      }

      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        setStatus('error')
        return
      }

      setError(describeApiFailure(cause, LIST_FALLBACK))
      setStatus('error')
    }
  }, [scope])

  // Смена скоупа — это другой серверный список, а не фильтр по уже загруженному.
  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- первая страница записей
    void load()

    return () => {
      versionRef.current += 1
      abortRef.current?.abort()
    }
  }, [load])

  const loadMore = useCallback(async () => {
    if (nextCursor === null || loadingMoreRef.current) {
      return
    }

    const current = versionRef.current
    loadingMoreRef.current = true
    setIsLoadingMore(true)
    setLoadMoreError(null)

    try {
      const page = await fetchBarberBookings(scope, nextCursor)

      if (versionRef.current !== current) {
        return
      }

      setItems((previous) => appendUnique(previous, page.items))
      setNextCursor(page.nextCursor)
    } catch (cause) {
      if (versionRef.current !== current) {
        return
      }

      if (cause instanceof ApiError && cause.status === 401) {
        setRequiresSignIn(true)
        return
      }

      // Ошибка дозагрузки не стирает уже показанные строки.
      setLoadMoreError(describeApiFailure(cause, LOAD_MORE_FALLBACK))
    } finally {
      if (versionRef.current === current) {
        loadingMoreRef.current = false
        setIsLoadingMore(false)
      }
    }
  }, [nextCursor, scope])

  /**
   * Одна мутация с общим гардом: пока запрос в полёте, строка заперта.
   *
   * `apply` меняет только свою строку после ответа сервера — оптимистичных правок
   * здесь нет, потому что статус окончательно решает домен.
   */
  const mutate = useCallback(
    async (
      id: string,
      request: (id: string) => Promise<void>,
      apply: (items: BarberBooking[]) => BarberBooking[]
    ) => {
      if (pendingMutationsRef.current.has(id)) {
        return
      }

      pendingMutationsRef.current.add(id)

      try {
        await request(id)
        setItems(apply)
      } finally {
        pendingMutationsRef.current.delete(id)
      }
    },
    []
  )

  const confirm = useCallback(
    async (id: string) => {
      await mutate(id, confirmBookingRequest, (previous) =>
        applyStatus(previous, id, 'Confirmed', scope)
      )
    },
    [mutate, scope]
  )

  const complete = useCallback(
    async (id: string) => {
      await mutate(id, completeBookingRequest, (previous) =>
        applyStatus(previous, id, 'Completed', scope)
      )
    },
    [mutate, scope]
  )

  const markNoShow = useCallback(
    async (id: string) => {
      await mutate(id, markBookingNoShow, (previous) =>
        applyStatus(previous, id, 'NoShow', scope)
      )
    },
    [mutate, scope]
  )

  const cancel = useCallback(
    async (id: string) => {
      await mutate(id, cancelBookingRequest, (previous) =>
        applyStatus(previous, id, 'Cancelled', scope)
      )
    },
    [mutate, scope]
  )

  const saveNote = useCallback(
    async (id: string, note: string) => {
      // Сервер обрезает и нормализует так же; клиент повторяет правило, чтобы строка
      // сразу показывала то, что он сохранит.
      const normalized = note.trim() === '' ? null : note.trim()

      await mutate(
        id,
        (bookingId) => saveBarberNote(bookingId, normalized ?? ''),
        (previous) =>
          previous.map((booking) =>
            booking.id === id
              ? { ...booking, barberNote: normalized }
              : booking
          )
      )
    },
    [mutate]
  )

  const reload = useCallback(() => {
    void load()
  }, [load])

  return {
    items,
    status,
    error,
    requiresSignIn,
    nextCursor,
    isLoadingMore,
    loadMoreError,
    reload,
    loadMore: () => {
      void loadMore()
    },
    confirm,
    complete,
    markNoShow,
    cancel,
    saveNote,
  }
}
