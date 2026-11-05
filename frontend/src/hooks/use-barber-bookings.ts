import { useCallback, useEffect, useRef, useState } from 'react'

import { usePagedList, type PagedListStatus } from '@/hooks/use-paged-list'
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
import { diffPageItems, type PageChange } from '@/lib/page-changes'

export type BarberBookingsController = {
  items: BarberBooking[]
  status: PagedListStatus
  /** Текст ошибки первой страницы; `null`, когда всё хорошо. */
  error: string | null
  /** 401 на списке: cookie-сессия кончилась, пока страница была открыта. */
  requiresSignIn: boolean
  /** Номер показанной страницы, с нуля. */
  pageIndex: number
  /** Сколько строк в предыдущих страницах — для подписи диапазона. */
  offset: number
  hasPrev: boolean
  hasNext: boolean
  /** Грузится следующая страница; текущая остаётся на экране. */
  isLoadingPage: boolean
  /** Ошибка следующей страницы: показанные строки не стираются. */
  pageError: string | null
  reload: () => void
  /**
   * Тихое обновление текущей страницы с разбором изменений: экран показывает
   * баннер только на новую или исчезнувшую запись. `null` — запрос не удался.
   */
  refresh: () => Promise<PageChange<BarberBooking> | null>
  isRefreshing: boolean
  /** Ошибка тихого обновления: строки остаются на экране. */
  refreshError: string | null
  /** Когда страница последний раз пришла с сервера. */
  updatedAt: number | null
  /** Одна из пяти мутаций в полёте: экран на это время останавливает опрос. */
  isMutating: boolean
  goPrev: () => void
  goNext: () => void
  confirm: (id: string) => Promise<void>
  complete: (id: string) => Promise<void>
  markNoShow: (id: string) => Promise<void>
  cancel: (id: string, reason: string) => Promise<void>
  saveNote: (id: string, note: string) => Promise<void>
}

const LIST_FALLBACK = 'Не удалось загрузить записи.'
const PAGE_FALLBACK = 'Не удалось загрузить ещё записи.'

const getBookingId = (booking: BarberBooking): string => booking.id

/** Для баннера важно только изменение статуса: остальные поля сервер не меняет. */
const statusChanged = (before: BarberBooking, after: BarberBooking): boolean =>
  before.status !== after.status

/** Терминальный статус уводит строку из «Ближайших»: история живёт в другом скоупе. */
function isTerminal(status: BarberBooking['status']): boolean {
  return status === 'Cancelled' || status === 'Completed' || status === 'NoShow'
}

/**
 * Новый вид строки: в «Ближайших» терминальный статус уводит её в историю, в
 * «Истории» она остаётся на месте — там этот статус и так уместен. `null` — строку
 * нужно убрать из списка (политику пустой страницы применяет общий хук).
 */
function applyStatus(
  booking: BarberBooking,
  next: BarberBooking['status'],
  scope: BarberBookingScope
): BarberBooking | null {
  if (scope === 'upcoming' && isTerminal(next)) {
    return null
  }

  return { ...booking, status: next }
}

/**
 * Записи клиентов барбера: страница, курсор и пять локальных мутаций.
 *
 * `upcoming` и `past` — разные серверные списки, поэтому `fetchPage` мемоизирован
 * по `scope`: смена вкладки сама грузит первую страницу и чистит кэш посещённых.
 * После мутации меняется только своя строка, без повторного `GET`: новый запрос
 * сдвинул бы курсор и вернул бы уже виденные строки. `409`/`404` пробрасываются в
 * строку или диалог — остальные строки при этом не трогаются.
 *
 * `refresh` — обратная операция для опроса: тихо перечитывает текущую страницу и
 * возвращает разницу, чтобы экран объявил новую или отменённую запись. Мутации при
 * этом не пересекаются с опросом: `isMutating` закрывает таймер на время запроса.
 */
export function useBarberBookings(
  scope: BarberBookingScope
): BarberBookingsController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchBarberBookings(scope, cursor, signal),
    [scope]
  )

  const {
    items,
    status,
    error,
    requiresSignIn,
    pageIndex,
    offset,
    hasPrev,
    hasNext,
    isLoadingPage,
    pageError,
    reload,
    refreshPage,
    isRefreshing,
    refreshError,
    updatedAt,
    goPrev,
    goNext,
    replaceItem,
    removeItem,
  } = usePagedList<BarberBooking>({
    getId: getBookingId,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
  })

  // id с запросом в полёте: второй клик по той же строке молча игнорируется.
  const pendingMutationsRef = useRef(new Set<string>())
  // Число мутаций в полёте: пока оно больше нуля, опрос не трогает список, иначе
  // ответ, прочитанный до коммита мутации, вернул бы строке прошлый статус.
  const [pendingMutations, setPendingMutations] = useState(0)
  // Последняя показанная версия строки по id. Ответ приходит, когда страница могла
  // уже смениться, а правка обязана примениться ко всем закэшированным страницам.
  const knownBookingsRef = useRef(new Map<string, BarberBooking>())
  // Строки текущей страницы до тихого обновления — база для «исчезла»/«изменилась».
  const currentPageRef = useRef<BarberBooking[]>([])

  useEffect(() => {
    currentPageRef.current = items

    for (const booking of items) {
      knownBookingsRef.current.set(booking.id, booking)
    }
  }, [items])

  /**
   * Тихое обновление для опроса и кнопки «Обновить».
   *
   * «Новая» — только строка, которой барбер ещё не видел: запись, приехавшая с
   * соседней страницы после сдвига, не должна объявляться новой.
   */
  const refresh = useCallback(async (): Promise<PageChange<BarberBooking> | null> => {
    const previousPage = currentPageRef.current
    const page = await refreshPage()

    if (page === null) {
      return null
    }

    const change = diffPageItems(
      previousPage,
      page.items,
      getBookingId,
      statusChanged
    )

    return {
      ...change,
      added: change.added.filter(
        (booking) => !knownBookingsRef.current.has(booking.id)
      ),
    }
  }, [refreshPage])

  /**
   * Одна мутация с общим гардом: пока запрос в полёте, строка заперта.
   *
   * `update` меняет только свою строку после ответа сервера — оптимистичных правок
   * здесь нет, потому что статус окончательно решает домен.
   */
  const mutate = useCallback(
    async (
      id: string,
      request: (id: string) => Promise<void>,
      update: (booking: BarberBooking) => BarberBooking | null
    ): Promise<void> => {
      if (pendingMutationsRef.current.has(id)) {
        return
      }

      pendingMutationsRef.current.add(id)
      setPendingMutations(pendingMutationsRef.current.size)

      try {
        await request(id)

        const booking = knownBookingsRef.current.get(id)

        if (booking === undefined) {
          return
        }

        const next = update(booking)

        if (next === null) {
          removeItem(id)
          return
        }

        replaceItem(id, next)
      } finally {
        pendingMutationsRef.current.delete(id)
        setPendingMutations(pendingMutationsRef.current.size)
      }
    },
    [removeItem, replaceItem]
  )

  const confirm = useCallback(
    (id: string) =>
      mutate(id, confirmBookingRequest, (booking) =>
        applyStatus(booking, 'Confirmed', scope)
      ),
    [mutate, scope]
  )

  const complete = useCallback(
    (id: string) =>
      mutate(id, completeBookingRequest, (booking) =>
        applyStatus(booking, 'Completed', scope)
      ),
    [mutate, scope]
  )

  const markNoShow = useCallback(
    (id: string) =>
      mutate(id, markBookingNoShow, (booking) =>
        applyStatus(booking, 'NoShow', scope)
      ),
    [mutate, scope]
  )

  const cancel = useCallback(
    (id: string, reason: string) =>
      mutate(
        id,
        (bookingId) => cancelBookingRequest(bookingId, reason),
        (booking) => {
          const next = applyStatus(booking, 'Cancelled', scope)

          return next === null
            ? null
            : { ...next, cancellationReason: reason.trim() || null }
        }
      ),
    [mutate, scope]
  )

  const saveNote = useCallback(
    async (id: string, note: string): Promise<void> => {
      // Сервер обрезает и нормализует так же; клиент повторяет правило, чтобы строка
      // сразу показывала то, что он сохранит.
      const normalized = note.trim() === '' ? null : note.trim()

      await mutate(
        id,
        (bookingId) => saveBarberNote(bookingId, normalized ?? ''),
        (booking) => ({ ...booking, barberNote: normalized })
      )
    },
    [mutate]
  )

  return {
    items,
    status,
    error,
    requiresSignIn,
    pageIndex,
    offset,
    hasPrev,
    hasNext,
    isLoadingPage,
    pageError,
    reload,
    refresh,
    isRefreshing,
    refreshError,
    updatedAt,
    isMutating: pendingMutations > 0,
    goPrev,
    goNext,
    confirm,
    complete,
    markNoShow,
    cancel,
    saveNote,
  }
}
