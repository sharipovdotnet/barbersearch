import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ProfileProvider } from '@/components/profile-provider'
import { useMyProfile } from '@/hooks/use-my-profile'
import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import type { MyProfile } from '@/lib/my-profile'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

const USER: SessionUser = {
  id: '9e000000-0000-4000-8000-000000000004',
  email: 'mark@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
}

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

/** Настоящая пара провайдеров: профиль читает контекст, сессия — заглушка. */
function wrapperFor(user: SessionUser) {
  const session: SessionContextValue = {
    status: 'authenticated',
    user,
    signIn: async () => user,
    signOut: async () => {},
    retrySession: () => {},
  }

  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <SessionContext.Provider value={session}>
        <ProfileProvider>{children}</ProfileProvider>
      </SessionContext.Provider>
    )
  }
}

describe('useMyProfile: аватар', () => {
  it('setAvatar правит общее состояние без второго GET', async () => {
    const gets = countRequests('/api/users/me')

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(profile(USER.id)))
    )

    const { result, rerender } = renderHook(() => useMyProfile(), {
      wrapper: wrapperFor(USER),
    })

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(gets()).toBe(1)

    act(() => {
      result.current.setAvatar(AVATAR_URL)
    })

    expect(result.current.profile?.avatarUrl).toBe(AVATAR_URL)
    expect(gets()).toBe(1)

    // Повторный рендер подписчика берёт то же значение из контекста, а не из сети.
    rerender()

    expect(result.current.profile?.avatarUrl).toBe(AVATAR_URL)
    expect(gets()).toBe(1)
  })

  it('save отправляет текущий avatarUrl после setAvatar', async () => {
    let body: Record<string, unknown> | null = null

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))),
      http.put(`/api/users/${USER.id}/profile`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>

        return new HttpResponse(null, { status: 204 })
      })
    )

    const { result } = renderHook(() => useMyProfile(), {
      wrapper: wrapperFor(USER),
    })

    await waitFor(() => expect(result.current.status).toBe('ready'))

    act(() => {
      result.current.setAvatar(AVATAR_URL)
    })

    await act(async () => {
      await result.current.save({
        firstName: ' Марк ',
        lastName: 'Правкин',
        phoneNumber: null,
        // Форма могла прислать устаревшее значение: провайдер обязан вернуть текущее.
        avatarUrl: null,
      })
    })

    expect(body).toStrictEqual({
      firstName: 'Марк',
      lastName: 'Правкин',
      phoneNumber: null,
      avatarUrl: AVATAR_URL,
    })
    expect(result.current.profile?.firstName).toBe('Марк')
    expect(result.current.profile?.avatarUrl).toBe(AVATAR_URL)
  })
})
