import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import type { MyProfile } from '@/lib/my-profile'
import { AccountPage } from '@/pages/account-page'
import { server } from '@/test/msw/server'

/**
 * Кабинет администратора — это «мои записи», а не админская выдача.
 *
 * Сервер без `userId` возвращает администратору все брони системы, поэтому обе
 * вкладки обязаны передавать id вошедшего пользователя.
 */

const ADMIN_ID = '55555555-5555-4555-8555-555555555555'

const profile: MyProfile = {
  id: ADMIN_ID,
  email: 'admin@barbersearch.test',
  firstName: 'Арина',
  lastName: 'Админова',
  phoneNumber: null,
  avatarUrl: null,
  role: 'Admin',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:00:00Z',
  lastLoginAt: null,
}

const user: SessionUser = {
  id: profile.id,
  email: profile.email,
  role: profile.role,
  barberProfileId: profile.barberProfileId,
  isEmailVerified: profile.isEmailVerified,
}

const session: SessionContextValue = {
  status: 'authenticated',
  user,
  signIn: async () => user,
  signOut: async () => {},
  retrySession: () => {},
}

beforeEach(() => {
  window.history.replaceState(null, '', '/account')
})

describe('AccountPage: только свои записи', () => {
  it('передаёт id администратора в «Предстоящие» и «Историю»', async () => {
    const userEvents = userEvent.setup()
    const requests: URL[] = []

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(profile)),
      http.get('/api/bookings', ({ request }) => {
        requests.push(new URL(request.url))

        return HttpResponse.json({ items: [], nextCursor: null })
      })
    )

    render(
      <SessionContext.Provider value={session}>
        <AccountPage />
      </SessionContext.Provider>
    )

    await userEvents.click(
      await screen.findByRole('tab', { name: 'История' })
    )

    await waitFor(() => {
      const scopes = requests.map((url) => url.searchParams.get('scope'))

      expect(scopes).toContain('upcoming')
      expect(scopes).toContain('past')
    })

    for (const scope of ['upcoming', 'past']) {
      const request = requests.find(
        (url) => url.searchParams.get('scope') === scope
      )

      expect(request?.searchParams.get('userId')).toBe(ADMIN_ID)
    }
  })
})
