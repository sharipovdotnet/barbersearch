import { describe, expect, it } from 'vitest'

import { isAdminUser } from '@/lib/response-guards'

/**
 * Строгий guard админской карточки: новые nullable-поля согласия (#42) принимаются
 * только как `null` или строка — число или объект означают, что сервер и клиент
 * разошлись в контракте, и карточку показывать нельзя.
 */
const DETAIL = {
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

describe('isAdminUser, запись согласия', () => {
  it('принимает запись с редакцией и временем', () => {
    expect(
      isAdminUser({
        ...DETAIL,
        consentPolicyVersion: '2026-11-03',
        consentedAt: '2026-11-03T14:05:00Z',
      })
    ).toBe(true)
  })

  it('принимает отсутствие записи: оба поля null', () => {
    expect(isAdminUser(DETAIL)).toBe(true)
  })

  it('отвергает число или объект вместо строки', () => {
    expect(isAdminUser({ ...DETAIL, consentPolicyVersion: 42 })).toBe(false)
    expect(isAdminUser({ ...DETAIL, consentedAt: { iso: '2026-11-03' } })).toBe(
      false
    )
  })

  it('отвергает ответ без полей согласия', () => {
    const { consentPolicyVersion, consentedAt, ...withoutConsent } = DETAIL

    expect(consentPolicyVersion).toBeNull()
    expect(consentedAt).toBeNull()
    expect(isAdminUser(withoutConsent)).toBe(false)
  })
})
