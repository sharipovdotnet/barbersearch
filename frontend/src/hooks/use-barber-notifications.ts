import { useCallback, useRef, useState } from 'react'

import { usePagedList, type PagedListStatus } from '@/hooks/use-paged-list'
import {
  describeNotificationsError,
  fetchNotifications,
  markAllRead as markAllReadRequest,
  markNotificationRead,
  type BarberNotification,
} from '@/lib/barber-notifications'

export type BarberNotificationsController = {
  items: BarberNotification[]
  status: PagedListStatus
  /** Ошибка первой страницы; `null`, когда всё хорошо. */
  error: string | null
  /** `401` на списке: cookie-сессия кончилась, пока страница была открыта. */
  requiresSignIn: boolean
  pageIndex: number
  /** Сколько строк в предыдущих страницах — для подписи диапазона. */
  offset: number
  hasPrev: boolean
  hasNext: boolean
  isLoadingPage: boolean
  /** Ошибка следующей страницы: показанные строки не стираются. */
  pageError: string | null
  reload: () => void
  goPrev: () => void
  goNext: () => void
  /** Непрочитанные на показанной странице: кнопка «Прочитать все» гаснет на нуле. */
  unreadCount: number
  /**
   * Отметить одно уведомление прочитанным и убрать точку.
   *
   * Ошибка не пробрасывается: строка всё равно открывает кабинет, а `404` значит, что
   * уведомление уже удалено. Повторный клик попробует ещё раз.
   */
  read: (id: string) => Promise<void>
  /**
   * `POST read-all`, локальная отметка строк и тихое перечитывание страницы.
   * `false` — запрос не удался, экран показывает `markAllError`.
   */
  markAllRead: () => Promise<boolean>
  isMarkingAllRead: boolean
  markAllError: string | null
}

const LIST_FALLBACK = 'Не удалось загрузить уведомления.'
const PAGE_FALLBACK = 'Не удалось загрузить ещё уведомления.'
const MARK_ALL_FALLBACK = 'Не удалось прочитать уведомления.'

const getNotificationId = (notification: BarberNotification): string =>
  notification.id

/**
 * Инбокс напоминаний: страница, «Прочитать все» и отметка одной строки.
 *
 * Список курсорный и читается через общий `usePagedList`, поэтому «Назад» не ходит в
 * сеть, а ошибка следующей страницы не стирает показанные строки. Одиночное чтение
 * меняет только свою строку: повторный `GET` сдвинул бы курсор и вернул уже виденное.
 * `read-all` сервер выполняет идемпотентно, после чего страница тихо перечитывается —
 * точки непрочитанного исчезают без скелетона.
 */
export function useBarberNotifications(): BarberNotificationsController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchNotifications(cursor, signal),
    []
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
    goPrev,
    goNext,
    replaceItem,
  } = usePagedList<BarberNotification>({
    getId: getNotificationId,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
  })

  // id с запросом в полёте: двойной клик по строке не шлёт второй POST.
  const pendingReadsRef = useRef(new Set<string>())
  // Ссылка, а не состояние: второй клик приходит раньше перерисовки.
  const markingAllRef = useRef(false)
  const [isMarkingAllRead, setIsMarkingAllRead] = useState(false)
  const [markAllError, setMarkAllError] = useState<string | null>(null)

  const read = useCallback(
    async (id: string): Promise<void> => {
      if (pendingReadsRef.current.has(id)) {
        return
      }

      const notification = items.find((item) => item.id === id)

      // Уже прочитанное не шлёт запрос: сервер идемпотентен, но точка уже скрыта.
      if (notification === undefined || notification.readAt !== null) {
        return
      }

      pendingReadsRef.current.add(id)

      try {
        await markNotificationRead(id)
        replaceItem(id, {
          ...notification,
          readAt: new Date().toISOString(),
        })
      } catch {
        // Строка всё равно откроется: чтение — фон, а не условие навигации.
      } finally {
        pendingReadsRef.current.delete(id)
      }
    },
    [items, replaceItem]
  )

  const markAllRead = useCallback(async (): Promise<boolean> => {
    if (markingAllRef.current) {
      return false
    }

    markingAllRef.current = true
    setIsMarkingAllRead(true)
    setMarkAllError(null)

    try {
      await markAllReadRequest()

      // Локальная отметка делает кнопку disabled сразу; серверный ответ ниже
      // подтверждает `readAt` и снимает точки без скелетона.
      const readAt = new Date().toISOString()

      for (const notification of items) {
        if (notification.readAt === null) {
          replaceItem(notification.id, { ...notification, readAt })
        }
      }

      await refreshPage()
      return true
    } catch (cause) {
      setMarkAllError(describeNotificationsError(cause, MARK_ALL_FALLBACK))
      return false
    } finally {
      markingAllRef.current = false
      setIsMarkingAllRead(false)
    }
  }, [items, refreshPage, replaceItem])

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
    goPrev,
    goNext,
    unreadCount: items.filter(
      (notification) => notification.readAt === null
    ).length,
    read,
    markAllRead,
    isMarkingAllRead,
    markAllError,
  }
}
