import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'

import {
  ProfileContext,
  type ProfileContextValue,
  type ProfileState,
} from '@/hooks/use-my-profile'
import { useSession } from '@/hooks/use-session'
import { ApiError } from '@/lib/api'
import { describeApiFailure } from '@/lib/errors'
import {
  fetchMyProfile,
  updateMyProfile,
  type MyProfile,
  type UpdateMyProfileInput,
} from '@/lib/my-profile'

/**
 * Общий профиль приложения: один `GET users/me` на авторизованную загрузку.
 *
 * Провайдер монтируется в `main.tsx` внутри `SessionProvider` и `I18nProvider`.
 * `HeaderAuth` и кабинет читают одно и то же состояние через `useMyProfile()`,
 * поэтому загрузка или удаление аватара в кабинете меняет шапку без перезагрузки.
 *
 * Сессией владеет только `SessionProvider`: на `401` провайдер выставляет
 * `requiresSignIn`, но не разлогинивает и не показывает ошибку в шапке.
 */

const INITIAL_STATE: ProfileState = {
  status: 'idle',
  profile: null,
  error: null,
  requiresSignIn: false,
}

const PROFILE_FALLBACK = 'Не удалось загрузить профиль.'

function isInitialState(state: ProfileState): boolean {
  return (
    state.status === 'idle' &&
    state.profile === null &&
    state.error === null &&
    !state.requiresSignIn
  )
}

export function ProfileProvider({ children }: { children: ReactNode }) {
  const { status: sessionStatus, user } = useSession()
  const userId = user?.id ?? null

  const [state, setState] = useState<ProfileState>(INITIAL_STATE)

  // Актуальное состояние для callbacks: `save`/`setAvatar` не должны зависеть
  // от замыкания прошлого рендера.
  const stateRef = useRef<ProfileState>(INITIAL_STATE)

  // Поколение отсекает поздний ответ прошлого пользователя или вышедшей сессии.
  const generationRef = useRef(0)

  // Один запрос на пользователя: StrictMode и повторный рендер его переиспользуют.
  const inFlightRef = useRef<{ userId: string; promise: Promise<void> } | null>(
    null
  )

  // Ответ после настоящего размонтирования не должен менять состояние. Отдельный
  // ref, а не cleanup загрузчика: StrictMode перезапускает эффекты дважды.
  const mountedRef = useRef(false)

  const applyState = useCallback((next: ProfileState) => {
    stateRef.current = next
    setState(next)
  }, [])

  const load = useCallback(
    (nextUserId: string): Promise<void> => {
      // Single-flight на пользователя: две проверки подряд видят один promise.
      if (inFlightRef.current?.userId === nextUserId) {
        return inFlightRef.current.promise
      }

      const generation = generationRef.current + 1
      generationRef.current = generation

      const previous = stateRef.current

      // Профиль прошлого аккаунта не показываем даже на время загрузки.
      applyState({
        status: 'loading',
        profile: previous.profile?.id === nextUserId ? previous.profile : null,
        error: null,
        requiresSignIn: false,
      })

      const promise = (async (): Promise<void> => {
        try {
          const profile = await fetchMyProfile()

          if (
            !mountedRef.current ||
            generationRef.current !== generation
          ) {
            return
          }

          applyState({
            status: 'ready',
            profile,
            error: null,
            requiresSignIn: false,
          })
        } catch (cause) {
          if (
            !mountedRef.current ||
            generationRef.current !== generation
          ) {
            return
          }

          // `401` — не ошибка профиля, а истёкшая сессия: её состояние ведёт
          // `SessionProvider`, провайдер не разлогинивает и не шумит в шапке.
          if (cause instanceof ApiError && cause.status === 401) {
            applyState({
              status: 'error',
              profile: null,
              error: null,
              requiresSignIn: true,
            })
            return
          }

          applyState({
            status: 'error',
            profile: null,
            error: describeApiFailure(cause, PROFILE_FALLBACK),
            requiresSignIn: false,
          })
        }
      })().finally(() => {
        if (inFlightRef.current?.promise === promise) {
          inFlightRef.current = null
        }
      })

      inFlightRef.current = { userId: nextUserId, promise }

      return promise
    },
    [applyState]
  )

  // Сброс без запроса нужен выходу и `unavailable`; поздний ответ отсечёт поколение.
  const reset = useCallback(() => {
    generationRef.current += 1
    inFlightRef.current = null
    applyState(INITIAL_STATE)
  }, [applyState])

  // Поднимается до загрузчика: StrictMode снимает и ставит эффекты по порядку.
  useEffect(() => {
    mountedRef.current = true

    return () => {
      mountedRef.current = false
    }
  }, [])

  useEffect(() => {
    if (sessionStatus === 'authenticated' && userId !== null) {
      // oxlint-disable-next-line react/set-state-in-effect -- профиль грузится после проверки сессии
      void load(userId)
      return
    }

    // Пока сессия проверяется, профиль не запрашиваем; после выхода — сбрасываем.
    if (!isInitialState(stateRef.current)) {
      // oxlint-disable-next-line react/set-state-in-effect -- выход/недоступность сессии сбрасывают профиль
      reset()
    }
  }, [load, reset, sessionStatus, userId])

  /** `PUT users/{id}/profile` заменяет поле целиком: текущий `avatarUrl` возвращаем. */
  const save = useCallback(
    async (input: UpdateMyProfileInput): Promise<MyProfile> => {
      const current = stateRef.current.profile

      if (!current || current.id !== userId) {
        throw new Error('Профиль ещё не загружен.')
      }

      const next: MyProfile = {
        ...current,
        firstName: input.firstName.trim(),
        lastName: input.lastName.trim(),
        phoneNumber: input.phoneNumber?.trim() || null,
      }

      await updateMyProfile(current.id, {
        firstName: next.firstName,
        lastName: next.lastName,
        phoneNumber: next.phoneNumber,
        avatarUrl: current.avatarUrl,
      })

      applyState({
        status: 'ready',
        profile: next,
        error: null,
        requiresSignIn: false,
      })

      return next
    },
    [applyState, userId]
  )

  /** Общий для всех подписчиков новый URL — без второго `GET users/me`. */
  const setAvatar = useCallback(
    (avatarUrl: string | null): void => {
      const current = stateRef.current.profile

      if (!current || current.id !== userId) {
        return
      }

      applyState({
        status: 'ready',
        profile: { ...current, avatarUrl },
        error: null,
        requiresSignIn: false,
      })
    },
    [applyState, userId]
  )

  const reload = useCallback((): Promise<void> => {
    if (sessionStatus !== 'authenticated' || userId === null) {
      return Promise.resolve()
    }

    return load(userId)
  }, [load, sessionStatus, userId])

  // Первый рендер после `authenticated` ещё не запустил эффект: кабинет должен
  // видеть `loading`, а не пустой экран (старый хук стартовал с `loading`).
  const status =
    sessionStatus === 'authenticated' && state.status === 'idle'
      ? 'loading'
      : state.status

  const value = useMemo<ProfileContextValue>(
    () => ({ ...state, status, reload, save, setAvatar }),
    [state, status, reload, save, setAvatar]
  )

  return (
    <ProfileContext.Provider value={value}>{children}</ProfileContext.Provider>
  )
}
