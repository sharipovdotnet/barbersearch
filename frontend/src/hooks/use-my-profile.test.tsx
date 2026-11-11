import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { useMyProfile } from '@/hooks/use-my-profile'
import type { MyProfile } from '@/lib/my-profile'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

function profile(id: string): MyProfile {
  return {
    id,
    email: 'mark@barbersearch.test',
    firstName: 'Марк',
    lastName: 'Правкин',
    phoneNumber: null,
    avatarUrl: null,
    role: 'Customer',
    barberProfileId: null,
    isEmailVerified: true,
    isPhoneVerified: false,
    createdAt: '2026-09-01T10:00:00Z',
    lastLoginAt: null,
  }
}

describe('useMyProfile: аватар', () => {
  it('setAvatar правит состояние и кэш без второго GET', async () => {
    const id = 'aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa'
    const gets = countRequests('/api/users/me')

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(profile(id)))
    )

    const first = renderHook(() => useMyProfile(id))

    await waitFor(() => expect(first.result.current.status).toBe('ready'))
    expect(gets()).toBe(1)

    act(() => {
      first.result.current.setAvatar(AVATAR_URL)
    })

    expect(first.result.current.profile?.avatarUrl).toBe(AVATAR_URL)
    expect(gets()).toBe(1)

    // Кэш модульный: следующий раздел кабинета берёт новый URL без запроса.
    first.unmount()

    const second = renderHook(() => useMyProfile(id))

    expect(second.result.current.status).toBe('ready')
    expect(second.result.current.profile?.avatarUrl).toBe(AVATAR_URL)
    expect(gets()).toBe(1)
  })

  it('save отправляет текущий avatarUrl после setAvatar', async () => {
    const id = 'bbbbbbbb-1111-4111-8111-bbbbbbbbbbbb'
    let body: Record<string, unknown> | null = null

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(profile(id))),
      http.put(`/api/users/${id}/profile`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useMyProfile(id))

    await waitFor(() => expect(result.current.status).toBe('ready'))

    act(() => {
      result.current.setAvatar(AVATAR_URL)
    })

    await act(async () => {
      await result.current.save({
        firstName: ' Марк ',
        lastName: 'Правкин',
        phoneNumber: null,
        avatarUrl: AVATAR_URL,
      })
    })

    expect(body).toStrictEqual({
      firstName: 'Марк',
      lastName: 'Правкин',
      phoneNumber: null,
      avatarUrl: AVATAR_URL,
    })
  })
})
