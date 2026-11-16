import { useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { SUGGESTIONS_DEBOUNCE_MS } from '@/lib/address-suggestions'

export type AddressSuggestionsStatus =
  | 'idle'
  | 'loading'
  | 'ready'
  | 'empty'
  | 'hidden'

export type AddressSuggestionsController<T> = {
  items: T[]
  status: AddressSuggestionsStatus
}

type SuggestionsState<T> = {
  /** Строка, к которой относится ответ: поздний ответ старого запроса не показываем. */
  key: string
  items: T[]
  status: Exclude<AddressSuggestionsStatus, 'idle'>
}

/**
 * Подсказки адреса с задержкой 250 мс.
 *
 * Запрос уходит только с минимальной длины строки: более короткую сервер отвергает
 * `400`. Каждый новый ввод отменяет прошлый запрос, номер поколения не даёт позднему
 * ответу перезаписать актуальный. Ошибка пользователю не показывается: подсказки —
 * необязательная помощь, и при недоступности форма молча остаётся текстовой.
 *
 * `hidden` — тихий отказ: после `409` (провайдер выключен или недоступен) запросы в
 * этой сессии диалога больше не делаются; `429` и сеть прячут только текущую строку,
 * и следующий ввод снова пробует сеть.
 */
export function useAddressSuggestions<T>(
  query: string,
  minLength: number,
  load: (query: string, signal: AbortSignal) => Promise<T[]>
): AddressSuggestionsController<T> {
  const key = query.trim()
  const [state, setState] = useState<SuggestionsState<T> | null>(null)
  const [isUnavailable, setIsUnavailable] = useState(false)
  const versionRef = useRef(0)
  const loadRef = useRef(load)

  // Функция загрузки приходит из JSX и меняет личность на каждый рендер: держим её
  // в ref, иначе эффект перезапускался бы на каждой перерисовке формы.
  useEffect(() => {
    loadRef.current = load
  })

  const current = state?.key === key ? state : null

  useEffect(() => {
    if (isUnavailable || key.length < minLength) {
      return
    }

    const controller = new AbortController()
    const version = versionRef.current + 1
    versionRef.current = version

    const timer = window.setTimeout(() => {
      loadRef
        .current(key, controller.signal)
        .then((items) => {
          if (versionRef.current !== version) {
            return
          }

          setState({
            key,
            items,
            status: items.length === 0 ? 'empty' : 'ready',
          })
        })
        .catch((cause: unknown) => {
          if (versionRef.current !== version || controller.signal.aborted) {
            return
          }

          // `409` — провайдер выключен или недоступен целиком: дальше не ходим.
          if (cause instanceof ApiError && cause.status === 409) {
            setIsUnavailable(true)
          }

          // Битое тело: `apiRequest` уже превратил несовпадение guard'а в `ApiError`
          // со статусом ответа. Сеть работает, подсказок нет — пустое состояние.
          if (cause instanceof ApiError && cause.status >= 200 && cause.status < 300) {
            setState({ key, items: [], status: 'empty' })
            return
          }

          setState({ key, items: [], status: 'hidden' })
        })
    }, SUGGESTIONS_DEBOUNCE_MS)

    return () => {
      window.clearTimeout(timer)
      versionRef.current += 1
      controller.abort()
    }
  }, [key, minLength, isUnavailable])

  if (isUnavailable) {
    return { items: [], status: 'hidden' }
  }

  if (key.length < minLength) {
    return { items: [], status: 'idle' }
  }

  return {
    items: current?.items ?? [],
    status: current?.status ?? 'loading',
  }
}
