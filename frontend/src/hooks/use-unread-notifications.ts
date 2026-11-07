import { useCallback, useEffect, useRef, useState } from 'react'

import { fetchUnreadCount } from '@/lib/barber-notifications'

/**
 * Период тихого обновления счётчика.
 *
 * Минута — компромисс для бейджа: напоминание о визите приходит за 30 минут, поэтому
 * отставание в минуту не критично, а открытый кабинет делает всего один дешёвый
 * запрос в минуту. `useAutoRefresh` здесь не подходит: у счётчика нет страницы,
 * которую нужно сохранять, и он должен обновляться ещё и на `focus` окна.
 */
export const UNREAD_NOTIFICATIONS_REFRESH_MS = 60_000

export type UnreadNotificationsOptions = {
  /** Колокольчик монтируется только после гейтов; флаг оставлен для тестов. */
  enabled?: boolean
  /** Период в миллисекундах; в тестах его уменьшают. */
  intervalMs?: number
}

export type UnreadNotificationsController = {
  /**
   * `null` — счётчик неизвестен: первый запрос ещё не ответил или упал. Бейдж в этом
   * случае скрыт, и никакой ошибки на экране нет.
   */
  count: number | null
  /** Тихое обновление по действию (возврат на вкладку, после «Прочитать все»). */
  refresh: () => void
}

/**
 * Счётчик непрочитанных для колокольчика.
 *
 * Один `GET` при монтировании, затем тихое обновление раз в минуту и при возврате на
 * вкладку (`visibilitychange` и `focus`). Ошибка не показывается: бейдж просто
 * исчезает, а следующий тик попробует снова. Запросы не накладываются друг на друга
 * — медленная сеть не создаёт очередь, а ответ отменённого запроса не меняет счётчик.
 */
export function useUnreadNotifications(
  options: UnreadNotificationsOptions = {}
): UnreadNotificationsController {
  const { enabled = true, intervalMs = UNREAD_NOTIFICATIONS_REFRESH_MS } = options
  const [count, setCount] = useState<number | null>(null)
  const mountedRef = useRef(true)
  const inFlightRef = useRef(false)
  const controllerRef = useRef<AbortController | null>(null)

  const request = useCallback(() => {
    if (!enabled || inFlightRef.current) {
      return
    }

    inFlightRef.current = true
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller

    void fetchUnreadCount(controller.signal)
      .then((payload) => {
        if (!mountedRef.current || controller.signal.aborted) {
          return
        }

        setCount(payload.count)
      })
      .catch(() => {
        // Ошибка счётчика не должна всплывать: бейдж просто скрыт.
        if (!mountedRef.current || controller.signal.aborted) {
          return
        }

        setCount(null)
      })
      .finally(() => {
        inFlightRef.current = false
      })
  }, [enabled])

  useEffect(() => {
    mountedRef.current = true

    if (!enabled) {
      return
    }

    request()

    const timer = window.setInterval(request, intervalMs)
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        request()
      }
    }

    document.addEventListener('visibilitychange', handleVisibilityChange)
    // Возврат в окно — тот же случай, что и возврат на вкладку; обработчик общий.
    window.addEventListener('focus', handleVisibilityChange)

    return () => {
      mountedRef.current = false
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      window.removeEventListener('focus', handleVisibilityChange)
      controllerRef.current?.abort()
      inFlightRef.current = false
    }
  }, [enabled, intervalMs, request])

  return { count, refresh: request }
}
