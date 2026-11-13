import { useCallback, useMemo } from 'react'

import type { BarberBookingsController } from '@/hooks/use-barber-bookings'
import { useNow } from '@/hooks/use-now'
import { t } from '@/i18n'
import {
  findDayGaps,
  findNextBooking,
  mergeDayBookings,
  type DayGap,
} from '@/lib/barber-day'
import {
  BARBER_BOOKINGS_PAGE_SIZE,
  type BarberBooking,
} from '@/lib/barber-bookings'
import type { BarberSlot } from '@/lib/barber-slots'
import { diffPageItems, type PageChange } from '@/lib/page-changes'

/** Одна из двух выборок упала, вторая отдала строки: день остаётся на экране. */
export const PARTIAL_ERROR = 'Не удалось обновить часть записей.'

const DAY_FALLBACK = 'Не удалось загрузить записи.'

export type BarberDayController = {
  /** День по возрастанию. */
  items: BarberBooking[]
  gaps: DayGap[]
  nextBooking: BarberBooking | null
  /** В дне ровно двадцать строк, а один из списков ещё может отдать продолжение. */
  isTruncated: boolean
  status: 'loading' | 'ready' | 'error'
  error: string | null
  /** Один список упал, второй отдал строки: лента остаётся, сверху предупреждение. */
  partialError: string | null
  /** Повторяет только упавший список. */
  retryFailed: () => void
  requiresSignIn: boolean
  isRefreshing: boolean
  refreshError: string | null
  updatedAt: number | null
  refresh: () => Promise<PageChange<BarberBooking> | null>
  reload: () => void
  confirm: (id: string) => Promise<void>
  complete: (id: string) => Promise<void>
  markNoShow: (id: string) => Promise<void>
  cancel: (id: string, reason: string) => Promise<void>
  saveNote: (id: string, note: string) => Promise<void>
  /** Перенос уходит в контроллер-владелец строки; второй список не перечитывается. */
  reschedule: (id: string, slot: BarberSlot) => Promise<void>
}

function isListLoading(controller: BarberBookingsController): boolean {
  return controller.status === 'loading' || controller.status === 'idle'
}

/** Свежесть дня — по самому свежему из двух списков. */
function latest(left: number | null, right: number | null): number | null {
  if (left === null) {
    return right
  }

  if (right === null) {
    return left
  }

  return Math.max(left, right)
}

/**
 * Новое содержимое списка по разнице страниц.
 *
 * `usePagedList` возвращает только diff, но дню нужен его собственный diff по
 * объединению двух списков: строка, ушедшая из «Ближайших» в «Историю» (например,
 * отменённая клиентом), остаётся в дне и не должна объявляться ни новой, ни
 * исчезнувшей.
 */
function applyChange(
  items: readonly BarberBooking[],
  change: PageChange<BarberBooking>
): BarberBooking[] {
  const updated = new Map(change.updated.map(({ after }) => [after.id, after]))
  const removed = new Set(change.removed.map((booking) => booking.id))

  const kept = items
    .filter((booking) => !removed.has(booking.id))
    .map((booking) => updated.get(booking.id) ?? booking)

  return [...kept, ...change.added]
}

const getId = (booking: BarberBooking): string => booking.id
const statusChanged = (left: BarberBooking, right: BarberBooking): boolean =>
  left.status !== right.status || left.startAt !== right.startAt

/**
 * Производный контроллер ленты дня.
 *
 * Хук не создаёт своих запросов: страница держит два существующих контроллера
 * (`useBarberBookings('upcoming')` и `useBarberBookings('past')`) и передаёт их
 * внутрь. Иначе переключение «Сегодня → Ближайшие» перезапрашивало бы те же данные,
 * а опрос и действия расходились бы с вкладками списка.
 *
 * Действие уходит в тот контроллер, в чьём списке строка; после терминального
 * действия второй список перечитывается — строка переезжает из «Ближайших» в
 * «Историю» и должна появиться в дне без ручного обновления.
 */
export function useBarberDay(
  upcoming: BarberBookingsController,
  past: BarberBookingsController
): BarberDayController {
  const now = useNow()
  const { items: upcomingItems, refresh: refreshUpcoming } = upcoming
  const { items: pastItems, refresh: refreshPast } = past

  const items = useMemo(
    () => mergeDayBookings(upcomingItems, pastItems, now),
    [upcomingItems, pastItems, now]
  )
  const gaps = useMemo(() => findDayGaps(items), [items])
  const nextBooking = useMemo(() => findNextBooking(items, now), [items, now])

  const hasFailed = upcoming.status === 'error' || past.status === 'error'
  const isLoading = isListLoading(upcoming) && isListLoading(past)

  const status: BarberDayController['status'] =
    items.length === 0 && hasFailed
      ? 'error'
      : items.length === 0 && isLoading
        ? 'loading'
        : 'ready'

  const error =
    status === 'error'
      ? (upcoming.error ?? past.error ?? t(DAY_FALLBACK))
      : null
  const partialError = hasFailed && items.length > 0 ? t(PARTIAL_ERROR) : null

  const retryFailed = useCallback(() => {
    if (upcoming.status === 'error') {
      upcoming.reload()
    }

    if (past.status === 'error') {
      past.reload()
    }
  }, [upcoming, past])

  const reload = useCallback(() => {
    upcoming.reload()
    past.reload()
  }, [upcoming, past])

  /**
   * Тихое обновление обоих списков и diff по объединённому дню.
   *
   * `null` — оба запроса не удались: объявлять нечего.
   */
  const refresh = useCallback(async (): Promise<PageChange<BarberBooking> | null> => {
    const moment = Date.now()
    const before = mergeDayBookings(upcoming.items, past.items, moment)

    const [upcomingChange, pastChange] = await Promise.all([
      refreshUpcoming(),
      refreshPast(),
    ])

    if (upcomingChange === null && pastChange === null) {
      return null
    }

    const after = mergeDayBookings(
      upcomingChange === null
        ? upcoming.items
        : applyChange(upcoming.items, upcomingChange),
      pastChange === null ? past.items : applyChange(past.items, pastChange),
      moment
    )

    return diffPageItems(before, after, getId, statusChanged)
  }, [refreshUpcoming, refreshPast, upcoming, past])

  /** Строка живёт в одном из двух списков: `id` уникальны. */
  const ownerOf = useCallback(
    (id: string): BarberBookingsController | null => {
      if (upcoming.items.some((booking) => booking.id === id)) {
        return upcoming
      }

      if (past.items.some((booking) => booking.id === id)) {
        return past
      }

      return null
    },
    [upcoming, past]
  )

  /** Терминальное действие уводит строку в другой скоуп: второй список перечитываем. */
  const runTerminal = useCallback(
    async (id: string, action: 'complete' | 'markNoShow' | 'cancel', reason = '') => {
      const owner = ownerOf(id)

      if (owner === null) {
        return
      }

      const other = owner === upcoming ? past : upcoming

      if (action === 'complete') {
        await owner.complete(id)
      } else if (action === 'markNoShow') {
        await owner.markNoShow(id)
      } else {
        await owner.cancel(id, reason)
      }

      other.reload()
    },
    [ownerOf, upcoming, past]
  )

  const confirm = useCallback(
    async (id: string): Promise<void> => {
      const owner = ownerOf(id)
      await owner?.confirm(id)
    },
    [ownerOf]
  )

  const complete = useCallback(
    (id: string) => runTerminal(id, 'complete'),
    [runTerminal]
  )

  const markNoShow = useCallback(
    (id: string) => runTerminal(id, 'markNoShow'),
    [runTerminal]
  )

  const cancel = useCallback(
    (id: string, reason: string) => runTerminal(id, 'cancel', reason),
    [runTerminal]
  )

  const saveNote = useCallback(
    async (id: string, note: string): Promise<void> => {
      const owner = ownerOf(id)
      await owner?.saveNote(id, note)
    },
    [ownerOf]
  )

  /**
   * Перенос не уводит строку в другой скоуп — визит остаётся активным, поэтому
   * второй список не перечитывается. День пересобирается из изменившейся строки и
   * сам уберёт её из «сегодня», если время уехало на другой день.
   */
  const reschedule = useCallback(
    async (id: string, slot: BarberSlot): Promise<void> => {
      const owner = ownerOf(id)
      await owner?.reschedule(id, slot)
    },
    [ownerOf]
  )

  const isTruncated =
    (upcoming.items.length === BARBER_BOOKINGS_PAGE_SIZE ||
      past.items.length === BARBER_BOOKINGS_PAGE_SIZE) &&
    items.length === BARBER_BOOKINGS_PAGE_SIZE

  return {
    items,
    gaps,
    nextBooking,
    isTruncated,
    status,
    error,
    partialError,
    retryFailed,
    requiresSignIn: upcoming.requiresSignIn || past.requiresSignIn,
    isRefreshing: upcoming.isRefreshing || past.isRefreshing,
    refreshError: upcoming.refreshError ?? past.refreshError,
    updatedAt: latest(upcoming.updatedAt, past.updatedAt),
    refresh,
    reload,
    confirm,
    complete,
    markNoShow,
    cancel,
    saveNote,
    reschedule,
  }
}
