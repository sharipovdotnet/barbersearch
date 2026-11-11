import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import { useI18n } from '@/i18n'
import {
  fetchMyProfile,
  updateMyProfile,
  type MyProfile,
  type UpdateMyProfileInput,
} from '@/lib/my-profile'

/**
 * Профиль кабинета: одно чтение `GET api/users/me` на сессию.
 *
 * Кэш модульный, поэтому переход между разделами кабинета не повторяет запрос. Ключ
 * кэша — id пользователя: после выхода и входа другим аккаунтом старый профиль не
 * должен показываться. `save` и `setAvatar` обновляют кэш и состояние без второго `GET`.
 */

type ProfileState = {
  status: 'loading' | 'ready' | 'error'
  profile: MyProfile | null
  error: string | null
  requiresSignIn: boolean
}

let cachedProfile: MyProfile | null = null

function readCache(userId: string): MyProfile | null {
  return cachedProfile?.id === userId ? cachedProfile : null
}

export function useMyProfile(userId: string) {
  const { t } = useI18n()
  const [state, setState] = useState<ProfileState>(() => {
    const cached = readCache(userId)

    return cached
      ? { status: 'ready', profile: cached, error: null, requiresSignIn: false }
      : { status: 'loading', profile: null, error: null, requiresSignIn: false }
  })

  // Номер поколения отсекает поздний ответ прошлого пользователя или размонтирования.
  const generation = useRef(0)

  const load = useCallback(async () => {
    const current = ++generation.current

    setState((previous) => ({
      status: 'loading',
      // Профиль прошлого аккаунта не показываем даже на время загрузки.
      profile:
        readCache(userId) ??
        (previous.profile?.id === userId ? previous.profile : null),
      error: null,
      requiresSignIn: false,
    }))

    try {
      const profile = await fetchMyProfile()

      if (current !== generation.current) {
        return
      }

      cachedProfile = profile
      setState({
        status: 'ready',
        profile,
        error: null,
        requiresSignIn: false,
      })
    } catch (error) {
      if (current !== generation.current) {
        return
      }

      const requiresSignIn = error instanceof ApiError && error.status === 401

      setState({
        status: 'error',
        profile: readCache(userId),
        error: requiresSignIn
          ? t('Сессия истекла: войдите снова, чтобы увидеть профиль.')
          : describeApiFailure(error, 'Не удалось загрузить профиль.'),
        requiresSignIn,
      })
    }
  }, [t, userId])

  useEffect(() => {
    if (readCache(userId)) {
      return
    }

    // oxlint-disable-next-line react/set-state-in-effect -- профиль грузится после монтирования каркаса
    void load()

    return () => {
      generation.current += 1
    }
  }, [load, userId])

  const save = useCallback(
    async (input: UpdateMyProfileInput): Promise<MyProfile> => {
      const currentProfile = cachedProfile

      if (!currentProfile || currentProfile.id !== userId) {
        throw new Error('Профиль ещё не загружен.')
      }

      const next: MyProfile = {
        ...currentProfile,
        firstName: input.firstName.trim(),
        lastName: input.lastName.trim(),
        phoneNumber: input.phoneNumber?.trim() || null,
      }

      await updateMyProfile(currentProfile.id, {
        firstName: next.firstName,
        lastName: next.lastName,
        phoneNumber: next.phoneNumber,
        // PUT заменяет поле целиком: отправляем текущий аватар, включая только что
        // загруженный `setAvatar`, иначе сохранение имени стёрло бы его.
        avatarUrl: currentProfile.avatarUrl,
      })

      cachedProfile = next
      setState({
        status: 'ready',
        profile: next,
        error: null,
        requiresSignIn: false,
      })

      return next
    },
    [userId]
  )

  /**
   * Новый аватар после загрузки или удаления — без второго `GET users/me`.
   *
   * Пишет и модульный кэш, и состояние: следующий раздел кабинета получит уже
   * новый URL, а `save()` отправит его обратно в `PUT profile`.
   */
  const setAvatar = useCallback(
    (avatarUrl: string | null): void => {
      const currentProfile = cachedProfile

      if (!currentProfile || currentProfile.id !== userId) {
        return
      }

      const next: MyProfile = { ...currentProfile, avatarUrl }

      cachedProfile = next
      setState({
        status: 'ready',
        profile: next,
        error: null,
        requiresSignIn: false,
      })
    },
    [userId]
  )

  return {
    ...state,
    reload: load,
    save,
    setAvatar,
  }
}
