import { createContext, useContext } from 'react'

import type { SessionUser, SignInCredentials } from '@/lib/auth'

/**
 * `unavailable` — сессию проверить не удалось (сеть, `5xx`, таймаут): это не выход
 * и не «пользователь не вошёл», поэтому гейты входа для него не показываются.
 */
export type SessionStatus =
  | 'loading'
  | 'authenticated'
  | 'anonymous'
  | 'unavailable'

export type SessionContextValue = {
  /** `loading` — сессия ещё проверяется, до этого нельзя показать «Войти» или профиль. */
  status: SessionStatus
  user: SessionUser | null
  signIn: (credentials: SignInCredentials) => Promise<void>
  signOut: () => Promise<void>
  /** Повторная полная проверка сессии — тот же bootstrap, включая подсказку и refresh. */
  retrySession: () => void
}

export const SessionContext = createContext<SessionContextValue | null>(null)

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext)

  if (!context) {
    throw new Error('useSession must be used inside SessionProvider')
  }

  return context
}
