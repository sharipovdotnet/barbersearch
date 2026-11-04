import { describe, expect, it } from 'vitest'

import {
  isAdminUser,
  isBarberBooking,
  isBooking,
} from '@/lib/response-guards'

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

const BOOKING = {
  id: '22222222-2222-4222-8222-222222222222',
  userId: '33333333-3333-4333-8333-333333333333',
  barberProfileId: '44444444-4444-4444-8444-444444444444',
  barberServiceId: '55555555-5555-4555-8555-555555555555',
  salonId: '66666666-6666-4666-8666-666666666666',
  startAt: '2026-12-01T10:00:00Z',
  endAt: '2026-12-01T10:30:00Z',
  price: 45,
  status: 'Cancelled',
  idempotencyKey: 'booking-key-1',
  customerNote: null,
  barberNote: null,
  cancellationReason: 'Барбер заболел',
  createdAt: '2026-11-01T10:00:00Z',
}

const BARBER_BOOKING = {
  id: '77777777-7777-4777-8777-777777777777',
  status: 'Cancelled',
  startAt: '2026-12-01T10:00:00Z',
  endAt: '2026-12-01T10:30:00Z',
  price: 45,
  customerNote: null,
  barberNote: null,
  cancellationReason: 'Барбер заболел',
  customer: { displayName: 'Иван Петров', phoneNumber: null },
  service: { name: 'Стрижка' },
  salon: {
    name: 'Салон',
    address: 'ул. Тестовая, 1',
    city: 'Берлин',
    timeZoneId: 'Europe/Berlin',
  },
}

describe('isBooking / isBarberBooking, причина отмены', () => {
  it('принимает строку и null в cancellationReason', () => {
    expect(isBooking(BOOKING)).toBe(true)
    expect(isBooking({ ...BOOKING, cancellationReason: null })).toBe(true)
    expect(isBarberBooking(BARBER_BOOKING)).toBe(true)
    expect(isBarberBooking({ ...BARBER_BOOKING, cancellationReason: null })).toBe(
      true
    )
  })

  it('отвергает ответ без cancellationReason', () => {
    const { cancellationReason, ...withoutReason } = BOOKING
    const { cancellationReason: barberReason, ...barberWithoutReason } =
      BARBER_BOOKING

    expect(cancellationReason).toBe('Барбер заболел')
    expect(barberReason).toBe('Барбер заболел')
    expect(isBooking(withoutReason)).toBe(false)
    expect(isBarberBooking(barberWithoutReason)).toBe(false)
  })

  it('отвергает не-строку вместо причины', () => {
    expect(isBooking({ ...BOOKING, cancellationReason: 42 })).toBe(false)
    expect(isBarberBooking({ ...BARBER_BOOKING, cancellationReason: {} })).toBe(
      false
    )
  })
})
