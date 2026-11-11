import { describe, expect, it } from 'vitest'

import {
  profileDisplayName,
  profileInitials,
  type MyProfile,
} from '@/lib/my-profile'
import { isMyProfile } from '@/lib/response-guards'

const PROFILE: MyProfile = {
  id: '9e000000-0000-4000-8000-000000000003',
  email: 'e2e-customer@barbersearch.test',
  firstName: 'Ева',
  lastName: 'Клиентова',
  phoneNumber: '+70000000003',
  avatarUrl: null,
  isAvatarBlocked: false,
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:02:00+00:00',
  lastLoginAt: '2026-11-02T14:00:00+00:00',
}

describe('guard профиля кабинета', () => {
  it('принимает ответ GET api/users/me', () => {
    expect(isMyProfile(PROFILE)).toBe(true)
  })

  it('не принимает неизвестную роль', () => {
    expect(isMyProfile({ ...PROFILE, role: 'Owner' })).toBe(false)
  })

  it('требует nullable-поля, а не их отсутствие', () => {
    expect(isMyProfile({ ...PROFILE, phoneNumber: undefined })).toBe(false)
    expect(isMyProfile({ ...PROFILE, lastLoginAt: undefined })).toBe(false)
  })

  it('не принимает пустой id', () => {
    expect(isMyProfile({ ...PROFILE, id: '' })).toBe(false)
  })
})

describe('отображение имени', () => {
  it('собирает полное имя', () => {
    expect(profileDisplayName(PROFILE)).toBe('Ева Клиентова')
    expect(profileInitials(PROFILE)).toBe('ЕК')
  })

  it('без имени не показывает пустоту', () => {
    expect(
      profileDisplayName({ ...PROFILE, firstName: '', lastName: '' })
    ).toBe(PROFILE.email)
    expect(
      profileInitials({ ...PROFILE, firstName: '', lastName: '' })
    ).toBe('E')
  })
})
