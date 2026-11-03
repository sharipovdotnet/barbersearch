import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { UserDetail } from '@/components/admin/user-detail'
import type { AdminUserDetail } from '@/lib/admin-users'

/**
 * Карточка пользователя с записью согласия либо без неё (старый или админский аккаунт).
 *
 * Проверяется только чтение: IP в ответе нет, редакция и время показываются в UTC.
 */
const BASE_USER: AdminUserDetail = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'anna@example.com',
  firstName: 'Анна',
  lastName: 'Иванова',
  phoneNumber: null,
  avatarUrl: null,
  role: 'Customer',
  isActive: true,
  isEmailVerified: false,
  isPhoneVerified: false,
  barberProfileId: null,
  barberProfile: null,
  lastLoginAt: null,
  lockoutEnd: null,
  failedLoginAttempts: 0,
  consentPolicyVersion: null,
  consentedAt: null,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
}

function renderDetail(user: AdminUserDetail) {
  render(
    <UserDetail
      user={user}
      isSelf={false}
      pendingAction={null}
      actionError={null}
      onChangeRole={() => {}}
      onBlock={() => {}}
      onUnblock={() => {}}
      onVerifyEmail={() => {}}
      onVerifyPhone={() => {}}
      onBack={() => {}}
    />
  )
}

describe('UserDetail, согласие на обработку персональных данных', () => {
  it('показывает редакцию Политики и время согласия в UTC', () => {
    renderDetail({
      ...BASE_USER,
      consentPolicyVersion: '2026-11-03',
      consentedAt: '2026-11-03T14:05:00Z',
    })

    expect(
      screen.getByText('Редакция 2026-11-03 · 03.11.2026, 14:05 UTC')
    ).toBeInTheDocument()
  })

  it('без записи согласия говорит «Записи нет», а не «Согласие не получено»', () => {
    renderDetail(BASE_USER)

    expect(
      screen.getByText('Записи нет — аккаунт создан до 03.11.2026')
    ).toBeInTheDocument()
    expect(screen.queryByText(/Согласие не получено/)).not.toBeInTheDocument()
  })

  it('не добавляет действий над согласием: только чтение', () => {
    renderDetail({
      ...BASE_USER,
      consentPolicyVersion: '2026-11-03',
      consentedAt: '2026-11-03T14:05:00Z',
    })

    expect(screen.queryByRole('button', { name: /соглас/i })).not.toBeInTheDocument()
  })
})
