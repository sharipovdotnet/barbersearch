import { createContext, useContext } from 'react'

import type { useMyProfile } from '@/hooks/use-my-profile'

/**
 * Состояние профиля, которое каркас кабинета загружает один раз.
 *
 * Вынесено из `account-shell.tsx`, чтобы файл компонентов экспортировал только
 * компоненты и Fast Refresh не терял состояние при правке каркаса.
 */

export type AccountProfileValue = ReturnType<typeof useMyProfile>

export const AccountProfileContext = createContext<AccountProfileValue | null>(
  null
)

/** Профиль кабинета для формы: загружен один раз каркасом, а не каждым экраном. */
export function useAccountProfile(): AccountProfileValue {
  const value = useContext(AccountProfileContext)

  if (!value) {
    throw new Error('useAccountProfile is only available inside AccountShell.')
  }

  return value
}
