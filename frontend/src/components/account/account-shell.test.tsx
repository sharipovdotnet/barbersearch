import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { AccountShell } from '@/components/account/account-shell'
import { ProfileProvider } from '@/components/profile-provider'
import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import type { MyProfile } from '@/lib/my-profile'
import { server } from '@/test/msw/server'
import { stubImageLoading } from '@/test/image'

function profile(
  id: string,
  isEmailVerified: boolean,
  avatarUrl: string | null = null
): MyProfile {
  return {
    id,
    email: 'client@barbersearch.test',
    firstName: 'Мария',
    lastName: 'Кузнецова',
    phoneNumber: null,
    avatarUrl,
    role: 'Customer',
    barberProfileId: null,
    isEmailVerified,
    isPhoneVerified: false,
    createdAt: '2026-09-01T10:00:00Z',
    lastLoginAt: null,
  }
}

function renderShell(value: MyProfile) {
  const user: SessionUser = {
    id: value.id,
    email: value.email,
    role: value.role,
    barberProfileId: value.barberProfileId,
    isEmailVerified: value.isEmailVerified,
  }
  const session: SessionContextValue = {
    status: 'authenticated',
    user,
    signIn: async () => user,
    signOut: async () => {},
    retrySession: () => {},
  }

  return render(
    <SessionContext.Provider value={session}>
      <ProfileProvider>
        <AccountShell
          user={user}
          section="security"
          title="Безопасность"
          description="Пароль и вход"
        >
          <p>Содержимое раздела</p>
        </AccountShell>
      </ProfileProvider>
    </SessionContext.Provider>
  )
}

describe('AccountShell: неподтверждённый email', () => {
  it('ведёт ссылкой в раздел безопасности, а не кнопкой', async () => {
    const value = profile('88888888-8888-4888-8888-888888888888', false)

    server.use(http.get('/api/users/me', () => HttpResponse.json(value)))

    renderShell(value)

    const link = await screen.findByRole('link', {
      name: 'Email не подтверждён',
    })
    expect(link).toHaveAttribute('href', '/account/security')
    expect(
      screen.queryByRole('button', { name: 'Email не подтверждён' })
    ).not.toBeInTheDocument()
  })

  it('подтверждённому адресу строку не показывает', async () => {
    const value = profile('99999999-9999-4999-8999-999999999999', true)

    server.use(http.get('/api/users/me', () => HttpResponse.json(value)))

    renderShell(value)

    expect(await screen.findByText('Мария Кузнецова')).toBeInTheDocument()
    expect(
      screen.queryByRole('link', { name: 'Email не подтверждён' })
    ).not.toBeInTheDocument()
  })

  it('показывает загруженный аватар вместо инициалов', async () => {
    const avatarUrl =
      '/api/media/avatars/88888888-8888-4888-8888-888888888888/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'
    const value = profile(
      '88888888-8888-4888-8888-888888888888',
      true,
      avatarUrl
    )

    stubImageLoading()
    server.use(http.get('/api/users/me', () => HttpResponse.json(value)))

    renderShell(value)

    await screen.findByText('Мария Кузнецова')

    await waitFor(() => {
      const image = document.querySelector(
        'aside [data-slot="avatar-image"]'
      )

      expect(image).toHaveAttribute('src', avatarUrl)
    })

    expect(screen.queryByText('МК')).not.toBeInTheDocument()
  })
})
