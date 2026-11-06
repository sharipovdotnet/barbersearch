import { useEffect, useRef } from 'react'

/**
 * Период тихого опроса экранов.
 *
 * Тридцать секунд — компромисс: новая запись появляется быстрее, чем барбер успевает
 * отложить телефон, а открытый экран делает всего два запроса в минуту. Сервер
 * отдаёт двадцать строк с курсором, поэтому цена тика для базы невелика.
 */
export const AUTO_REFRESH_INTERVAL_MS = 30_000

export type AutoRefreshOptions = {
  /** Опрос идёт только когда экран действительно показывает свежий список. */
  enabled: boolean
  /** Период в миллисекундах; в тестах его уменьшают. */
  intervalMs?: number
  /**
   * `false` — не опрашивать по таймеру, а обновлять только при возврате на вкладку.
   * Так живут справочники, которым не нужен тик, но нужна свежесть после переключения.
   */
  poll?: boolean
  /** Тихая перезагрузка. Промис важен: следующий тик ждёт завершения прошлого. */
  onRefresh: () => void | Promise<void>
}

/**
 * Фоновое обновление открытого экрана.
 *
 * По умолчанию работает по таймеру, но `poll: false` оставляет только возврат
 * на вкладку: справочнику не нужен тик, а свежесть после переключения нужна.
 *
 * Скрытая вкладка не тратит запросы: тик проверяет `document.visibilityState`, а
 * возвращение к вкладке обновляет список сразу, не дожидаясь периода. Один тик не
 * накладывается на другой — медленная сеть не создаёт очередь запросов.
 *
 * Слушателя `focus` нет намеренно: он срабатывает на каждый клик по окну и превратил
 * бы опрос в поток запросов. `visibilitychange` покрывает переход на другую вкладку и
 * сворачивание окна — именно те случаи, когда данные успевают устареть.
 */
export function useAutoRefresh({
  enabled,
  intervalMs = AUTO_REFRESH_INTERVAL_MS,
  poll = true,
  onRefresh,
}: AutoRefreshOptions): void {
  // Колбэк меняется на каждом рендере экрана; таймер не должен из-за этого перезапускаться.
  const callbackRef = useRef(onRefresh)

  useEffect(() => {
    callbackRef.current = onRefresh
  }, [onRefresh])

  useEffect(() => {
    if (!enabled) {
      return
    }

    let inFlight = false

    const run = () => {
      if (inFlight || document.visibilityState !== 'visible') {
        return
      }

      inFlight = true

      void Promise.resolve(callbackRef.current()).finally(() => {
        inFlight = false
      })
    }

    const timer = poll ? window.setInterval(run, intervalMs) : null
    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        run()
      }
    }

    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      if (timer !== null) {
        window.clearInterval(timer)
      }
      document.removeEventListener('visibilitychange', handleVisibilityChange)
    }
  }, [enabled, intervalMs, poll])
}
