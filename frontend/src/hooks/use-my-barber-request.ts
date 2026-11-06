import { useCallback, useEffect, useState } from 'react'

import {
  describeMyBarberRequestError,
  fetchMyBarberRequest,
  isBarberRequestUnauthorized,
  type BarberRequest,
} from '@/lib/barber-requests'

export type MyBarberRequestStatus = 'idle' | 'loading' | 'ready' | 'error'

export type MyBarberRequestController = {
  /** Своя последняя заявка; `null` — её ещё нет. */
  request: BarberRequest | null
  status: MyBarberRequestStatus
  error: string | null
  /** `401`: сессия закончилась, экран-гейт покажет вход (карточка в кабинете молчит). */
  requiresSignIn: boolean
  reload: () => void
}

type Loaded = {
  request: BarberRequest | null
}

/**
 * Своя заявка «Стать барбером»: одно чтение `GET /barber-requests/mine`.
 *
 * Хук монтируется только после проверок сессии, поэтому анонимный посетитель не
 * делает запрос. `enabled` оставлен для экранов, которые решают это сами; `401`
 * отделён от текста ошибки, потому что гейт входа уже выше.
 */
export function useMyBarberRequest(
  options: { enabled?: boolean } = {}
): MyBarberRequestController {
  const { enabled = true } = options
  const [reloadToken, setReloadToken] = useState(0)
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [requiresSignIn, setRequiresSignIn] = useState(false)

  useEffect(() => {
    if (!enabled) {
      return
    }

    const controller = new AbortController()
    let isActive = true

    fetchMyBarberRequest(controller.signal)
      .then((response) => {
        if (!isActive) {
          return
        }

        setLoaded({ request: response.request })
        setError(null)
        setRequiresSignIn(false)
      })
      .catch((cause: unknown) => {
        if (!isActive || controller.signal.aborted) {
          return
        }

        if (isBarberRequestUnauthorized(cause)) {
          setRequiresSignIn(true)
          setError(null)
          return
        }

        setError(describeMyBarberRequestError(cause))
      })

    return () => {
      isActive = false
      controller.abort()
    }
  }, [enabled, reloadToken])

  const reload = useCallback(() => {
    setLoaded(null)
    setError(null)
    setRequiresSignIn(false)
    setReloadToken((current) => current + 1)
  }, [])

  if (!enabled) {
    return {
      request: null,
      status: 'idle',
      error: null,
      requiresSignIn: false,
      reload,
    }
  }

  return {
    request: loaded?.request ?? null,
    status: error !== null ? 'error' : loaded === null ? 'loading' : 'ready',
    error,
    requiresSignIn,
    reload,
  }
}
