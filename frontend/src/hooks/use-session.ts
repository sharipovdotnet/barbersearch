import { createContext, useContext } from 'react'

import type { SessionUser, SignInCredentials } from '@/lib/auth'

export type SessionStatus = 'loading' | 'authenticated' | 'anonymous'

export type SessionContextValue = {
  /** `loading` — сессия ещё проверяется, до этого нельзя показать «Войти» или профиль. */
  status: SessionStatus
  user: SessionUser | null
  signIn: (credentials: SignInCredentials) => Promise<void>
  signOut: () => Promise<void>
}

export const SessionContext = createContext<SessionContextValue | null>(null)

export function useSession(): SessionContextValue {
  const context = useContext(SessionContext)

  if (!context) {
    throw new Error('useSession must be used inside SessionProvider')
  }

  return context
}
