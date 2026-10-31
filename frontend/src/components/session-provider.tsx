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

  useEffect(() => {
    statusRef.current = status
  }, [status])

  // Восстанавливаем сессию по cookie при загрузке страницы: сервер — единственный источник
  // правды, локального хранилища токенов нет. Refresh зовём только по подсказке «Запомнить
  // меня»: без неё заход анонима не должен тратить лимитер на заведомо неудачную попытку.
  useEffect(() => {
    let isActive = true

    const bootstrap = async () => {
      let currentUser: SessionUser | null
      try {
        currentUser = await fetchSession()
      } catch {
        // API недоступен: считаем сессию анонимной, а попытка входа покажет сетевую ошибку.
        if (isActive) {
          setUser(null)
          setStatus('anonymous')
        }
        return
      }

      if (!isActive) {
        return
      }

      if (currentUser) {
        setUser(currentUser)
        setStatus('authenticated')
        return
      }

      if (!hasRememberHint()) {
        setUser(null)
        setStatus('anonymous')
        return
      }

      const outcome = await refreshSession()
      if (!isActive) {
        return
      }

      if (outcome.kind !== 'refreshed') {
        // `terminal` уже снял подсказку; `transient` её не трогает — перезагрузка повторит.
        setUser(null)
        setStatus('anonymous')
        return
      }

      try {
        const recovered = await fetchSession()
        if (!isActive) {
          return
        }

        if (recovered) {
          setUser(recovered)
          setStatus('authenticated')
          return
        }

        // Cookie только что обновлена, а `/auth/me` снова `401` — второй refresh не шлём.
        clearRememberHint()
        setUser(null)
        setStatus('anonymous')
      } catch {
        // Сбой второго запроса — это не признак мёртвой сессии; подсказку не трогаем.
        if (isActive) {
          setUser(null)
          setStatus('anonymous')
        }
      }
    }

    void bootstrap()

    return () => {
      isActive = false
    }
  }, [])

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
    () => ({ status, user, signIn, signOut }),
    [status, user, signIn, signOut]
  )

  return (
    <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
  )
}
