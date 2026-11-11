import { createContext, useContext } from 'react'

import type { MyProfile, UpdateMyProfileInput } from '@/lib/my-profile'
import type { PhotoModerationStatus } from '@/lib/photo-moderation'

/**
 * Профиль текущего пользователя для всех поверхностей приложения.
 *
 * Читается из `ProfileProvider` (`src/components/profile-provider.tsx`), который
 * смонтирован один раз в `main.tsx`: шапка и кабинет видят один и тот же профиль,
 * поэтому загрузка или удаление аватара сразу меняет обе поверхности. Отдельного
 * модульного кэша больше нет — иначе шапка о мутации не узнала бы.
 */

export type ProfileStatus = 'idle' | 'loading' | 'ready' | 'error'

export type ProfileState = {
  /** `idle` — сессии нет или она ещё не проверена; запроса в этом состоянии нет. */
  status: ProfileStatus
  profile: MyProfile | null
  error: string | null
  /** `401` на `GET users/me`: сессией владеет `SessionProvider`, провайдер её не трогает. */
  requiresSignIn: boolean
}

export type ProfileContextValue = ProfileState & {
  /** Повторное чтение профиля; при уже идущем запросе отдаёт его же (single-flight). */
  reload: () => Promise<void>
  /** Сохраняет личные данные и возвращает обновлённый профиль; второй `GET` не нужен. */
  save: (input: UpdateMyProfileInput) => Promise<MyProfile>
  /**
   * Новый аватар после загрузки/удаления — общий для всех подписчиков, без `GET`.
   * Статус приходит вместе с URL: после загрузки — `Pending`, после удаления — `Approved`.
   */
  setAvatar: (
    avatarUrl: string | null,
    status: PhotoModerationStatus
  ) => void
}

export const ProfileContext = createContext<ProfileContextValue | null>(null)

/** Состояние профиля для шапки, кабинета и формы. Вне провайдера — ошибка. */
export function useMyProfile(): ProfileContextValue {
  const value = useContext(ProfileContext)

  if (!value) {
    throw new Error('useMyProfile must be used inside ProfileProvider')
  }

  return value
}
