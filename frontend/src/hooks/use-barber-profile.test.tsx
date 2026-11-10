import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useOwnBarberProfile } from '@/hooks/use-barber-profile'
import { problemDetails, testBarberProfile } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const PROFILE_URL = `/api/barber-profiles/${testBarberProfile.id}`

describe('useOwnBarberProfile', () => {
  it('читает профиль и перечитывает его по reload()', async () => {
    let requests = 0

    server.use(
      http.get(PROFILE_URL, () => {
        requests += 1

        return HttpResponse.json({
          ...testBarberProfile,
          displayName: requests === 1 ? 'Иван Петров' : 'Пётр Иванов',
        })
      })
    )

    const { result } = renderHook(() =>
      useOwnBarberProfile(testBarberProfile.id)
    )

    expect(result.current.status).toBe('loading')

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    expect(result.current.profile?.displayName).toBe('Иван Петров')
    expect(result.current.error).toBeNull()
    expect(result.current.isRefreshing).toBe(false)

    act(() => {
      result.current.reload()
    })

    await waitFor(() => {
      expect(result.current.profile?.displayName).toBe('Пётр Иванов')
    })

    expect(requests).toBeGreaterThanOrEqual(2)
    expect(result.current.status).toBe('ready')
    expect(result.current.isRefreshing).toBe(false)
  })

  it('ошибка повторного чтения не прячет уже загруженную карточку', async () => {
    let first = true

    server.use(
      http.get(PROFILE_URL, () => {
        if (first) {
          first = false

          return HttpResponse.json(testBarberProfile)
        }

        return HttpResponse.json(problemDetails(500), { status: 500 })
      })
    )

    const { result } = renderHook(() =>
      useOwnBarberProfile(testBarberProfile.id)
    )

    await waitFor(() => {
      expect(result.current.status).toBe('ready')
    })

    act(() => {
      result.current.reload()
    })

    await waitFor(() => {
      expect(result.current.error).toBe('Не удалось загрузить профиль.')
    })

    expect(result.current.status).toBe('ready')
    expect(result.current.profile).toStrictEqual(testBarberProfile)
    expect(result.current.isRefreshing).toBe(false)
  })

  it('первое чтение с битым телом — ошибка без профиля', async () => {
    server.use(
      http.get(PROFILE_URL, () =>
        HttpResponse.json({ ...testBarberProfile, services: 'битое' })
      )
    )

    const { result } = renderHook(() =>
      useOwnBarberProfile(testBarberProfile.id)
    )

    await waitFor(() => {
      expect(result.current.status).toBe('error')
    })

    expect(result.current.profile).toBeNull()
    expect(result.current.error).toBe('Не удалось загрузить профиль.')
    expect(result.current.isRefreshing).toBe(false)
  })
})
