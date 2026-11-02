import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
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
 * должен показываться. `save` обновляет кэш и состояние без второго `GET`.
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
          ? 'Сессия истекла: войдите снова, чтобы увидеть профиль.'
          : describeApiFailure(error, 'Не удалось загрузить профиль.'),
        requiresSignIn,
      })
    }
  }, [userId])

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
        // Аватар не редактируется, но PUT заменяет поле целиком.
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

  return {
    ...state,
    reload: load,
    save,
  }
}
