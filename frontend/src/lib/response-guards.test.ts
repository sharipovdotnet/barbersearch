import { describe, expect, it } from 'vitest'

import {
  isAdminSalonRow,
  isAdminUser,
  isBarberBooking,
  isBarberBookingOptions,
  isBarberSalonSummary,
  isBarberSearchItem,
  isBooking,
  isSalonDirectoryItem,
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

const PROFILE_SALON = {
  id: '88888888-8888-4888-8888-888888888888',
  salonId: '99999999-9999-4999-8999-999999999999',
  name: 'Кабинет на Ленина',
  address: 'ул. Ленина, 1',
  city: 'Екатеринбург',
  country: 'Россия',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'SoloStudio',
  isPrimary: true,
  workingSchedules: [],
}

const SEARCH_ITEM = {
  id: '11111111-1111-4111-8111-111111111111',
  displayName: 'Тимур Барберов',
  grade: 'SeniorBarber',
  yearsOfExperience: 8,
  languages: ['Ru'],
  rating: null,
  reviewsCount: 0,
  isVerified: true,
  primarySalonId: '99999999-9999-4999-8999-999999999999',
  primarySalonName: 'На дому',
  primaryPlaceKind: 'Home',
  city: 'Москва',
  additionalServices: [],
}

const ADMIN_SALON = {
  id: '22222222-2222-4222-8222-222222222222',
  name: 'Барбершоп на Ленина',
  address: 'ул. Ленина, 1',
  city: 'Екатеринбург',
  postalCode: null,
  country: 'Россия',
  timeZoneId: 'Asia/Yekaterinburg',
  latitude: 56.8389,
  longitude: 60.6057,
  phoneNumber: null,
  websiteUrl: null,
  isActive: true,
  moderationStatus: 'Active',
  kind: 'Salon',
  isListed: true,
  rejectionReason: null,
  reviewedAt: null,
  ownerEmail: null,
  usageCount: 1,
}

const SUMMARY = {
  salonId: '99999999-9999-4999-8999-999999999999',
  name: 'Кабинет на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'SoloStudio',
  isListed: false,
  isPrimary: true,
  country: 'Россия',
  postalCode: null,
  latitude: 56.8389,
  longitude: 60.6057,
  phoneNumber: null,
  websiteUrl: null,
  ownerUserId: null,
  moderationStatus: 'Active',
  rejectionReason: null,
}

const PICKER_ITEM = {
  id: '33333333-3333-4333-8333-333333333333',
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  kind: 'Salon',
}

describe('kind места в guard-ах', () => {
  it('принимает известный kind во всех контрактах с местом', () => {
    expect(
      isBarberBookingOptions({ services: [], salons: [PROFILE_SALON] })
    ).toBe(true)
    expect(isBarberSearchItem(SEARCH_ITEM)).toBe(true)
    expect(
      isBarberSearchItem({ ...SEARCH_ITEM, primaryPlaceKind: null })
    ).toBe(true)
    expect(isAdminSalonRow(ADMIN_SALON)).toBe(true)
    expect(isBarberSalonSummary(SUMMARY)).toBe(true)
    expect(isSalonDirectoryItem(PICKER_ITEM)).toBe(true)
  })

  it('отвергает неизвестный kind: молчаливого прочерка нет', () => {
    expect(
      isBarberBookingOptions({
        services: [],
        salons: [{ ...PROFILE_SALON, kind: 'Penthouse' }],
      })
    ).toBe(false)
    expect(
      isBarberSearchItem({ ...SEARCH_ITEM, primaryPlaceKind: 'Penthouse' })
    ).toBe(false)
    expect(isAdminSalonRow({ ...ADMIN_SALON, kind: 4 })).toBe(false)
    expect(isBarberSalonSummary({ ...SUMMARY, kind: 'Penthouse' })).toBe(false)
    expect(isSalonDirectoryItem({ ...PICKER_ITEM, kind: 'Penthouse' })).toBe(
      false
    )
  })

  it('требует isListed у владельческого списка и админской строки', () => {
    const { isListed: _summaryListed, ...summaryWithoutListed } = SUMMARY
    const { isListed: _adminListed, ...adminWithoutListed } = ADMIN_SALON

    expect(_summaryListed).toBe(false)
    expect(_adminListed).toBe(true)
    expect(isBarberSalonSummary(summaryWithoutListed)).toBe(false)
    expect(isAdminSalonRow(adminWithoutListed)).toBe(false)
  })
})
