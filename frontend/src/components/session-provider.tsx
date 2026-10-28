import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'

import {
  SessionContext,
  type SessionContextValue,
  type SessionStatus,
} from '@/hooks/use-session'
import {
  fetchSession,
  signIn as signInRequest,
  signOut as signOutRequest,
  type SessionUser,
  type SignInCredentials,
} from '@/lib/auth'

export function SessionProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [status, setStatus] = useState<SessionStatus>('loading')

  // Восстанавливаем сессию по cookie при загрузке страницы: сервер — единственный источник
  // правды, локального хранилища токенов нет.
  useEffect(() => {
    let isActive = true

    fetchSession()
      .then((currentUser) => {
        if (!isActive) {
          return
        }

        setUser(currentUser)
        setStatus(currentUser ? 'authenticated' : 'anonymous')
      })
      .catch(() => {
        if (!isActive) {
          return
        }

        // API недоступен: считаем сессию анонимной, а попытка входа покажет сетевую ошибку.
        setUser(null)
        setStatus('anonymous')
      })

    return () => {
      isActive = false
    }
  }, [])

  const signIn = useCallback(async (credentials: SignInCredentials) => {
    const currentUser = await signInRequest(credentials)
    setUser(currentUser)
    setStatus('authenticated')
  }, [])

  const signOut = useCallback(async () => {
    try {
      await signOutRequest()
    } finally {
      // Выход из интерфейса не должен зависеть от ответа сервера: cookie всё равно истечёт.
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
