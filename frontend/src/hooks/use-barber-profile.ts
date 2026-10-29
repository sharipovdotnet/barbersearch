import { useCallback, useEffect, useRef, useState } from 'react'

import { ApiError } from '@/lib/api'
import { isBarberId } from '@/lib/barber-path'
import {
  fetchBarberProfile,
  readCachedBarberProfile,
  type BarberProfile,
} from '@/lib/catalog'
import { describeApiFailure } from '@/lib/errors'

export type BarberProfileStatus = 'loading' | 'ready' | 'error' | 'notFound'

export type BarberProfileController = {
  profile: BarberProfile | null
  status: BarberProfileStatus
  error: string | null
  /** `404`/`400` либо невалидный GUID: повторять такой запрос бессмысленно. */
  notFound: boolean
  retry: () => void
}

const PROFILE_FALLBACK = 'Не удалось загрузить профиль барбера.'

type ProfileState = {
  /** Какому id принадлежит состояние: при смене адреса старые данные не показываем. */
  id: string
  profile: BarberProfile | null
  status: BarberProfileStatus
  error: string | null
}

function initialState(id: string): ProfileState {
  if (!isBarberId(id)) {
    return { id, profile: null, status: 'notFound', error: null }
  }

  const cached = readCachedBarberProfile(id)

  if (cached) {
    return { id, profile: cached, status: 'ready', error: null }
  }

  return { id, profile: null, status: 'loading', error: null }
}

/** Скрытый, удалённый или невалидный профиль сервер отдаёт как `404`/`400`. */
function isMissingProfile(cause: unknown): boolean {
  return (
    cause instanceof ApiError && (cause.status === 404 || cause.status === 400)
  )
}

/**
 * Публичная карточка барбера.
 *
 * Кэш общий с кабинетом, поэтому уже загруженный профиль показывается сразу.
 * Каждый запрос получает свой `AbortController` и номер поколения: поздний ответ
 * прошлого id не может перезаписать новый профиль, а размонтирование отменяет запрос.
 */
export function useBarberProfile(id: string): BarberProfileController {
  const [state, setState] = useState<ProfileState>(() => initialState(id))
  const [attempts, setAttempts] = useState({ id, count: 0 })
  const versionRef = useRef(0)

  const attempt = attempts.id === id ? attempts.count : 0
  // Смена id ещё до эффекта: иначе на долю кадра виден профиль прошлого барбера.
  const current = state.id === id ? state : initialState(id)

  useEffect(() => {
    if (!isBarberId(id)) {
      return
    }

    if (attempt === 0 && readCachedBarberProfile(id)) {
      return
    }

    const version = versionRef.current + 1
    versionRef.current = version
    const controller = new AbortController()

    fetchBarberProfile(id, controller.signal)
      .then((profile) => {
        if (versionRef.current !== version) {
          return
        }

        setState({ id, profile, status: 'ready', error: null })
      })
      .catch((cause: unknown) => {
        if (versionRef.current !== version || controller.signal.aborted) {
          return
        }

        if (isMissingProfile(cause)) {
          setState({ id, profile: null, status: 'notFound', error: null })
          return
        }

        setState({
          id,
          profile: null,
          status: 'error',
          error: describeApiFailure(cause, PROFILE_FALLBACK),
        })
      })

    return () => {
      // Поздний ответ отменённого запроса уже не пройдёт проверку поколения.
      versionRef.current += 1
      controller.abort()
    }
  }, [id, attempt])

  const retry = useCallback(() => {
    setAttempts((previous) => ({
      id,
      count: previous.id === id ? previous.count + 1 : 1,
    }))
    setState({ id, profile: null, status: 'loading', error: null })
  }, [id])

  return {
    profile: current.profile,
    status: current.status,
    error: current.error,
    notFound: current.status === 'notFound',
    retry,
  }
}
