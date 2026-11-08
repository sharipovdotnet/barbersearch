import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { AccountShell } from '@/components/account/account-shell'
import type { MyProfile } from '@/lib/my-profile'
import { server } from '@/test/msw/server'

function profile(id: string, isEmailVerified: boolean): MyProfile {
  return {
    id,
    email: 'client@barbersearch.test',
    firstName: 'Мария',
    lastName: 'Кузнецова',
    phoneNumber: null,
    avatarUrl: null,
    role: 'Customer',
    barberProfileId: null,
    isEmailVerified,
    isPhoneVerified: false,
    createdAt: '2026-09-01T10:00:00Z',
    lastLoginAt: null,
  }
}

function renderShell(value: MyProfile) {
  return render(
    <AccountShell
      user={{
        id: value.id,
        email: value.email,
        role: value.role,
        barberProfileId: value.barberProfileId,
        isEmailVerified: value.isEmailVerified,
      }}
      section="security"
      title="Безопасность"
      description="Пароль и вход"
    >
      <p>Содержимое раздела</p>
    </AccountShell>
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
})
