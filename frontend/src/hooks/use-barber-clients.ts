import { useCallback, useEffect, useRef, useState } from 'react'

import {
  MIN_BARBER_CLIENT_SEARCH,
  searchBarberClients,
  type BarberClient,
} from '@/lib/barber-clients'
import { describeApiFailure } from '@/lib/errors'

export type BarberClientsStatus = 'idle' | 'loading' | 'ready' | 'error'

export type BarberClientsController = {
  clients: BarberClient[]
  status: BarberClientsStatus
  error: string | null
  /** Повтор последнего запроса: сеть, `429` и `5xx`. */
  retry: () => void
}

type ClientsState = {
  /** Строка, к которой относится состояние: ответ старого поиска не показываем. */
  key: string
  clients: BarberClient[]
  status: BarberClientsStatus
  error: string | null
}

const FALLBACK = 'Не удалось найти клиентов.'
const DEBOUNCE_MS = 300

function initialState(key: string): ClientsState {
  return key.length < MIN_BARBER_CLIENT_SEARCH
    ? { key, clients: [], status: 'idle', error: null }
    : { key, clients: [], status: 'loading', error: null }
}

/**
 * Поиск клиента с задержкой 300 мс.
 *
 * Запрос уходит только с трёх символов: сервер отвечает `400 barberClient.search.tooShort`
 * на более короткую строку, а до этого форма не должна светить ни одним контактом.
 * Каждый новый ввод отменяет прошлый запрос; номер поколения не даёт позднему ответу
 * перезаписать актуальный. Подсказка не кэшируется: клиент мог сменить телефон, а форма
 * живёт недолго.
 */
export function useBarberClients(search: string): BarberClientsController {
  const key = search.trim()
  const [state, setState] = useState<ClientsState>(() => initialState(key))
  const [attempts, setAttempts] = useState({ key, count: 0 })
  const versionRef = useRef(0)

  const attempt = attempts.key === key ? attempts.count : 0
  const current = state.key === key ? state : initialState(key)

  useEffect(() => {
    if (key.length < MIN_BARBER_CLIENT_SEARCH) {
      return
    }

    const controller = new AbortController()
    const version = versionRef.current + 1
    versionRef.current = version

    const timer = window.setTimeout(() => {
      searchBarberClients(key, controller.signal)
        .then((clients) => {
          if (versionRef.current !== version) {
            return
          }

          setState({ key, clients, status: 'ready', error: null })
        })
        .catch((cause: unknown) => {
          if (versionRef.current !== version || controller.signal.aborted) {
            return
          }

          setState({
            key,
            clients: [],
            status: 'error',
            error: describeApiFailure(cause, FALLBACK),
          })
        })
    }, DEBOUNCE_MS)

    return () => {
      window.clearTimeout(timer)
      versionRef.current += 1
      controller.abort()
    }
  }, [key, attempt])

  // Повтор в том же поколении: строка не менялась, меняется только номер попытки.
  const retry = useCallback(() => {
    if (key.length < MIN_BARBER_CLIENT_SEARCH) {
      return
    }

    setState(initialState(key))
    setAttempts((previous) => ({
      key,
      count: previous.key === key ? previous.count + 1 : 1,
    }))
  }, [key])

  return {
    clients: current.clients,
    status: current.status,
    error: current.error,
    retry,
  }
}
