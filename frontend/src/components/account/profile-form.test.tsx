import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import {
  AccountProfileContext,
  type AccountProfileValue,
} from '@/components/account/account-profile-context'
import { ProfileForm } from '@/components/account/profile-form'
import { ApiError } from '@/lib/api'
import type { MyProfile, UpdateMyProfileInput } from '@/lib/my-profile'

/**
 * Форма профиля: сохранение только изменённого, клиентская проверка имён
 * и привязка серверных кодов к полям.
 */

const PROFILE: MyProfile = {
  id: '9e000000-0000-4000-8000-000000000003',
  email: 'e2e-customer@barbersearch.test',
  firstName: 'Мария',
  lastName: 'Кузнецова',
  phoneNumber: '+49 170 1234567',
  avatarUrl: null,
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:02:00+00:00',
  lastLoginAt: '2026-11-02T14:00:00+00:00',
}

function renderForm() {
  const save = vi.fn(
    async (input: UpdateMyProfileInput): Promise<MyProfile> => ({
      ...PROFILE,
      ...input,
    })
  )

  const value: AccountProfileValue = {
    status: 'ready',
    profile: PROFILE,
    error: null,
    requiresSignIn: false,
    reload: vi.fn(async () => {}),
    save,
    setAvatar: vi.fn(),
  }

  render(
    <AccountProfileContext.Provider value={value}>
      <ProfileForm profile={PROFILE} />
    </AccountProfileContext.Provider>
  )

  return { save }
}

describe('ProfileForm', () => {
  it('не предлагает сохранить неизменённую форму', () => {
    renderForm()

    expect(screen.getByRole('button', { name: 'Сохранить' })).toBeDisabled()
    expect(screen.getByText(PROFILE.email)).toBeVisible()
  })

  it('не отправляет пустое имя', async () => {
    const user = userEvent.setup()
    const { save } = renderForm()

    await user.clear(screen.getByLabelText('Имя'))
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(screen.getByText('Введите имя.')).toBeVisible()
    expect(save).not.toHaveBeenCalled()
  })

  it('отправляет обрезанные значения и сохраняет аватар', async () => {
    const user = userEvent.setup()
    const { save } = renderForm()
    const phone = screen.getByLabelText('Телефон')

    await user.clear(phone)
    await user.type(phone, '  +49 111  ')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1))
    expect(save).toHaveBeenCalledWith({
      firstName: 'Мария',
      lastName: 'Кузнецова',
      phoneNumber: '+49 111',
      avatarUrl: null,
    })
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Изменения сохранены.'
    )
  })

  it('показывает серверную ошибку имени у поля', async () => {
    const user = userEvent.setup()
    const { save } = renderForm()

    save.mockRejectedValueOnce(
      new ApiError(
        400,
        { errorCodes: ['user.updateProfile.firstName'] },
        'Invalid first name.'
      )
    )

    await user.type(screen.getByLabelText('Имя'), '!')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Введите имя.')).toBeVisible()
  })

  it('показывает сетевую ошибку формой, а не у поля', async () => {
    const user = userEvent.setup()
    const { save } = renderForm()

    save.mockRejectedValueOnce(new ApiError(0, null, 'Network error'))

    await user.type(screen.getByLabelText('Имя'), '!')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(
      await screen.findByText(
        'Нет связи с сервером. Проверьте подключение и повторите попытку.'
      )
    ).toBeVisible()
  })
})
