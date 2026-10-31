import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'

import {
  SessionContext,
  type SessionContextValue,
  type SessionStatus,
} from '@/hooks/use-session'
import { setUnauthorizedHandler } from '@/lib/api'
import {
  fetchSession,
  signIn as signInRequest,
  signOut as signOutRequest,
  type SessionUser,
  type SignInCredentials,
} from '@/lib/auth'
import {
  clearRememberHint,
  hasRememberHint,
  markRememberedSession,
} from '@/lib/session-hint'
import { refreshSession, resetSessionRefresh } from '@/lib/session-refresh'

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [status, setStatus] = useState<SessionStatus>('loading')

  // Статус и мёртвая сессия нужны обработчику `401` без пересоздания на каждый рендер.
  const statusRef = useRef<SessionStatus>('loading')
  const sessionDeadRef = useRef(false)

  // Одна проверка сессии на провайдер: StrictMode и двойной клик не дают второй сети.
  const bootstrapRef = useRef<Promise<void> | null>(null)
  const mountedRef = useRef(false)

  useEffect(() => {
    statusRef.current = status
  }, [status])

  /** Поздний ответ размонтированного провайдера не должен менять его состояние. */
  const applySession = useCallback(
    (nextUser: SessionUser | null, nextStatus: SessionStatus) => {
      if (!mountedRef.current) {
        return
      }

      setUser(nextUser)
      setStatus(nextStatus)
    },
    []
  )

  /**
   * Полная проверка сессии.
   *
   * `anonymous` ставится только по авторитетному `401`; сеть, `5xx` и `transient`
   * при refresh — это `unavailable`, «проверить не удалось».
   */
  const bootstrap = useCallback((): Promise<void> => {
    if (bootstrapRef.current) {
      return bootstrapRef.current
    }

    const run = (async () => {
      try {
        let currentUser: SessionUser | null
        try {
          currentUser = await fetchSession()
        } catch {
          applySession(null, 'unavailable')
          return
        }

        if (!mountedRef.current) {
          return
        }

        if (currentUser) {
          applySession(currentUser, 'authenticated')
          return
        }

        if (!hasRememberHint()) {
          applySession(null, 'anonymous')
          return
        }

        const outcome = await refreshSession()
        if (!mountedRef.current) {
          return
        }

        if (outcome.kind === 'transient') {
          // Подсказку не трогаем: «Повторить» или перезагрузка проверят сессию снова.
          applySession(null, 'unavailable')
          return
        }

        if (outcome.kind === 'terminal') {
          // `terminal` уже снял подсказку и липкий до успешного входа.
          applySession(null, 'anonymous')
          return
        }

        try {
          const recovered = await fetchSession()
          if (!mountedRef.current) {
            return
          }

          if (recovered) {
            applySession(recovered, 'authenticated')
            return
          }

          // Cookie только что обновлена, а `/auth/me` снова `401` — второй refresh не шлём.
          clearRememberHint()
          applySession(null, 'anonymous')
        } catch {
          // Сбой второго запроса — не признак мёртвой сессии; подсказку не трогаем.
          applySession(null, 'unavailable')
        }
      } finally {
        bootstrapRef.current = null
      }
    })()

    bootstrapRef.current = run
    return run
  }, [applySession])

  // Восстанавливаем сессию по cookie при загрузке страницы: сервер — единственный источник
  // правды, локального хранилища токенов нет. Refresh зовём только по подсказке «Запомнить
  // меня»: без неё заход анонима не должен тратить лимитер на заведомо неудачную попытку.
  useEffect(() => {
    mountedRef.current = true
    void bootstrap()

    return () => {
      mountedRef.current = false
    }
  }, [bootstrap])

  /** «Повторить» из шапки и экранов: одна проверка, скелетоны на время неё. */
  const retrySession = useCallback(() => {
    if (bootstrapRef.current) {
      return
    }

    setStatus('loading')
    statusRef.current = 'loading'
    void bootstrap()
  }, [bootstrap])

  // Прозрачное продление для данных: транспорт зовёт обработчик на `401` и повторяет запрос.
  useEffect(() => {
    setUnauthorizedHandler(async () => {
      if (statusRef.current !== 'authenticated' || sessionDeadRef.current) {
        return { retry: false }
      }

      const outcome = await refreshSession()

      if (outcome.kind === 'refreshed') {
        return { retry: true }
      }

      if (outcome.kind === 'terminal') {
        sessionDeadRef.current = true
        setUser(null)
        setStatus('anonymous')
        return { retry: false }
      }

      // Временный сбой: сессия и email в шапке живы, экран покажет сбой транспорта.
      return { retry: false, error: outcome.error }
    })

    return () => {
      setUnauthorizedHandler(null)
    }
  }, [])

  const signIn = useCallback(async (credentials: SignInCredentials) => {
    const currentUser = await signInRequest(credentials)

    resetSessionRefresh()
    sessionDeadRef.current = false

    if (credentials.rememberMe) {
      markRememberedSession()
    } else {
      // Снимаем чужую старую подсказку, иначе bootstrap восстановит «незапомненную» сессию.
      clearRememberHint()
    }

    setUser(currentUser)
    setStatus('authenticated')
  }, [])

  const signOut = useCallback(async () => {
    try {
      await signOutRequest()
    } finally {
      // Выход из интерфейса не должен зависеть от ответа сервера: cookie всё равно истечёт.
      clearRememberHint()
      setUser(null)
      setStatus('anonymous')
    }
  }, [])

  const value = useMemo<SessionContextValue>(
    () => ({ status, user, signIn, signOut, retrySession }),
    [status, user, signIn, signOut, retrySession]
  )

  return (
    <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
  )
}
