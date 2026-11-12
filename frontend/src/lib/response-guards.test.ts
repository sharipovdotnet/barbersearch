import { describe, expect, it } from 'vitest'

import {
  isAdminAuditPage,
  isAdminAvatarPhoto,
  isAdminBarber,
  isAdminBarberRow,
  isAdminBooking,
  isAdminPortfolioPhoto,
  isAdminSalonRow,
  isAdminSummary,
  isAdminUser,
  isAdminUserRow,
  isAvatarUploadResponse,
  isBarberBooking,
  isBarberBookingOptions,
  isBarberNotification,
  isBarberNotificationPage,
  isBarberProfile,
  isBarberSalonSummary,
  isBarberSearchItem,
  isBooking,
  isCreatedResource,
  isHairServiceOption,
  isMyProfile,
  isNotificationSettings,
  isPortfolioPhoto,
  isSalonDirectoryItem,
  isTelegramLink,
  isUnreadNotificationsPayload,
} from '@/lib/response-guards'
import { testBarberProfile } from '@/test/msw/handlers'

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
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  avatarRejectionReason: null,
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
  hasReview: false,
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
  customer: { displayName: 'Иван Петров', phoneNumber: null, avatarUrl: null },
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

describe('isBooking, отметка об отзыве', () => {
  it('принимает только булев hasReview', () => {
    expect(isBooking({ ...BOOKING, hasReview: true })).toBe(true)
    expect(isBooking({ ...BOOKING, hasReview: false })).toBe(true)
    expect(isBooking({ ...BOOKING, hasReview: 'true' })).toBe(false)
  })

  it('отвергает ответ без hasReview: флаг решает, показывать ли звезду', () => {
    const { hasReview, ...withoutReview } = BOOKING

    expect(hasReview).toBe(false)
    expect(isBooking(withoutReview)).toBe(false)
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
  avatarUrl: null,
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

const UPCOMING_NOTIFICATION = {
  id: '11111111-1111-4111-8111-111111111111',
  kind: 'Upcoming',
  createdAt: '2026-11-07T09:02:00Z',
  readAt: null,
  bookingId: '22222222-2222-4222-8222-222222222222',
  startAt: '2026-11-07T09:00:00Z',
  serviceName: 'стрижка',
  salonName: 'Барбершоп на Ленина',
  salonTimeZoneId: 'Asia/Yekaterinburg',
  bookingsCount: null,
  delivery: 'Sent',
}

const DIGEST_NOTIFICATION = {
  id: '33333333-3333-4333-8333-333333333333',
  kind: 'Digest',
  createdAt: '2026-11-07T03:00:00Z',
  readAt: '2026-11-07T04:00:00Z',
  bookingId: null,
  startAt: '2026-11-07T05:00:00Z',
  serviceName: 'стрижка',
  salonName: 'Барбершоп на Ленина',
  salonTimeZoneId: 'Asia/Yekaterinburg',
  bookingsCount: 4,
  delivery: 'Pending',
}

describe('guard-ы уведомлений', () => {
  it('принимает напоминание о визите и утренний дайджест', () => {
    expect(isBarberNotification(UPCOMING_NOTIFICATION)).toBe(true)
    expect(isBarberNotification(DIGEST_NOTIFICATION)).toBe(true)
    expect(
      isBarberNotificationPage({
        items: [UPCOMING_NOTIFICATION, DIGEST_NOTIFICATION],
        nextCursor: 'cursor-2',
        extra: 'поле игнорируется',
      })
    ).toBe(true)
  })

  it('отвергает неизвестные kind и delivery', () => {
    expect(
      isBarberNotification({ ...UPCOMING_NOTIFICATION, kind: 'Reminder' })
    ).toBe(false)
    expect(
      isBarberNotification({ ...UPCOMING_NOTIFICATION, delivery: 'Unknown' })
    ).toBe(false)
    expect(
      isBarberNotification({ ...UPCOMING_NOTIFICATION, delivery: 1 })
    ).toBe(false)
  })

  it('соблюдает инварианты вида: у визита есть бронь, у дайджеста — счётчик', () => {
    expect(
      isBarberNotification({ ...UPCOMING_NOTIFICATION, bookingId: null })
    ).toBe(false)
    expect(
      isBarberNotification({ ...UPCOMING_NOTIFICATION, bookingsCount: 0 })
    ).toBe(false)
    expect(
      isBarberNotification({ ...DIGEST_NOTIFICATION, bookingId: UPCOMING_NOTIFICATION.bookingId })
    ).toBe(false)
    expect(
      isBarberNotification({ ...DIGEST_NOTIFICATION, bookingsCount: null })
    ).toBe(false)
    expect(
      isBarberNotification({ ...DIGEST_NOTIFICATION, bookingsCount: -1 })
    ).toBe(false)
  })

  it('отвергает битую страницу и не-скалярные поля', () => {
    expect(
      isBarberNotificationPage({
        items: [{ ...UPCOMING_NOTIFICATION, startAt: { iso: '2026-11-07' } }],
        nextCursor: null,
      })
    ).toBe(false)
    expect(
      isBarberNotificationPage({
        items: [UPCOMING_NOTIFICATION],
        nextCursor: 2,
      })
    ).toBe(false)
  })

  it('принимает счётчик непрочитанных и отвергает дробное или отрицательное', () => {
    expect(isUnreadNotificationsPayload({ count: 0 })).toBe(true)
    expect(isUnreadNotificationsPayload({ count: 3 })).toBe(true)
    expect(isUnreadNotificationsPayload({ count: -1 })).toBe(false)
    expect(isUnreadNotificationsPayload({ count: 1.5 })).toBe(false)
    expect(isUnreadNotificationsPayload({ count: '3' })).toBe(false)
    expect(isUnreadNotificationsPayload({})).toBe(false)
  })

  it('принимает настройки с digestTime HH:mm и только 15/30/60', () => {
    const settings = {
      isEnabled: true,
      digestEnabled: true,
      digestTime: '08:00',
      leadMinutes: 30,
      telegramLinked: false,
      telegramLinkedAt: null,
      botConfigured: true,
    }

    expect(isNotificationSettings(settings)).toBe(true)
    expect(isNotificationSettings({ ...settings, leadMinutes: 60 })).toBe(true)
    expect(
      isNotificationSettings({
        ...settings,
        telegramLinked: true,
        telegramLinkedAt: '2026-11-07T09:00:00Z',
      })
    ).toBe(true)
    expect(isNotificationSettings({ ...settings, leadMinutes: 45 })).toBe(false)
    expect(isNotificationSettings({ ...settings, digestTime: '08:00:00' })).toBe(
      false
    )
    expect(isNotificationSettings({ ...settings, digestTime: '8:00' })).toBe(
      false
    )
    expect(isNotificationSettings({ ...settings, botConfigured: 'yes' })).toBe(
      false
    )
    const { botConfigured: _bot, ...withoutBot } = settings
    expect(_bot).toBe(true)
    expect(isNotificationSettings(withoutBot)).toBe(false)
  })

  it('принимает ответ привязки Telegram и отвергает пустой код', () => {
    expect(
      isTelegramLink({
        code: 'ABCDEF',
        deepLink: 'https://t.me/barbersearch_bot?start=ABCDEF',
        expiresAt: '2026-11-07T09:10:00Z',
      })
    ).toBe(true)
    expect(
      isTelegramLink({
        code: '',
        deepLink: 'https://t.me/barbersearch_bot?start=',
        expiresAt: '2026-11-07T09:10:00Z',
      })
    ).toBe(false)
  })
})

describe('isHairServiceOption, длительность по умолчанию', () => {
  const SERVICE = {
    id: '22222222-2222-4222-8222-222222222222',
    name: 'Стрижка',
    category: 'Haircut',
    defaultDurationMinutes: 45,
  }

  it('принимает строку с длительностью и лишними полями', () => {
    expect(isHairServiceOption({ ...SERVICE, description: 'любое' })).toBe(true)
  })

  it('отвергает строку без длительности или с нечислом', () => {
    const { defaultDurationMinutes, ...withoutDuration } = SERVICE

    expect(defaultDurationMinutes).toBe(45)
    expect(isHairServiceOption(withoutDuration)).toBe(false)
    expect(isHairServiceOption({ ...SERVICE, defaultDurationMinutes: '45' })).toBe(
      false
    )
  })
})

const PORTFOLIO_PHOTO = {
  id: '9e200000-0000-4000-8000-000000000001',
  url: '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/abcdef0123456789abcdef0123456789.jpg',
  thumbnailUrl: null,
  description: 'Фейд',
  sortOrder: 0,
  isPublished: true,
  isBlocked: false,
  moderationStatus: 'Approved',
  rejectionReason: null,
}

/**
 * Фотография портфолио: все поля читает и галерея, и кабинет, и админка.
 *
 * `url` — непустая строка (относительный `/api/media/...` или абсолютный внешний
 * URL), подпись и миниатюра — `null` или строка, флаги — boolean.
 */
describe('isPortfolioPhoto', () => {
  it('принимает фотографию с подписью и без, лишние поля игнорирует', () => {
    expect(isPortfolioPhoto(PORTFOLIO_PHOTO)).toBe(true)
    expect(
      isPortfolioPhoto({
        ...PORTFOLIO_PHOTO,
        description: null,
        thumbnailUrl: 'https://example.test/thumb.jpg',
        extra: 'поле расширения',
      })
    ).toBe(true)
  })

  it('отвергает пустой url и неверные nullability', () => {
    expect(isPortfolioPhoto({ ...PORTFOLIO_PHOTO, url: '' })).toBe(false)
    expect(
      isPortfolioPhoto({ ...PORTFOLIO_PHOTO, thumbnailUrl: undefined })
    ).toBe(false)
    expect(
      isPortfolioPhoto({ ...PORTFOLIO_PHOTO, description: 42 })
    ).toBe(false)
  })

  it('отвергает нечисловой sortOrder и строковые флаги', () => {
    expect(isPortfolioPhoto({ ...PORTFOLIO_PHOTO, sortOrder: '0' })).toBe(false)
    expect(
      isPortfolioPhoto({ ...PORTFOLIO_PHOTO, isPublished: 'true' })
    ).toBe(false)
    expect(
      isPortfolioPhoto({ ...PORTFOLIO_PHOTO, isBlocked: 1 })
    ).toBe(false)
  })

  it('отвергает отсутствующие и нестроковые обязательные поля', () => {
    const { isBlocked, ...withoutBlocked } = PORTFOLIO_PHOTO
    const { id, ...withoutId } = PORTFOLIO_PHOTO

    expect(isBlocked).toBe(false)
    expect(id).toBe(PORTFOLIO_PHOTO.id)
    expect(isPortfolioPhoto(withoutBlocked)).toBe(false)
    expect(isPortfolioPhoto(withoutId)).toBe(false)
    expect(isPortfolioPhoto(null)).toBe(false)
    expect(isPortfolioPhoto([])).toBe(false)
  })
})

const ADMIN_BARBER_DETAIL = {
  id: '9e800000-0000-4000-8000-000000000001',
  userId: '9e000000-0000-4000-8000-000000000001',
  displayName: 'Артём Соколов',
  email: 'artem@barbersearch.test',
  phoneNumber: null,
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  avatarRejectionReason: null,
  grade: 'ArtDirector',
  bio: null,
  yearsOfExperience: 5,
  rating: null,
  reviewsCount: 0,
  isActive: true,
  isVerified: false,
  readiness: {
    isProfileFilled: false,
    isEmailVerified: true,
    servicesCount: 0,
    salonsCount: 0,
    scheduleDaysCount: 0,
    photosCount: 0,
  },
  photos: [],
  createdAt: '2026-09-28T15:12:00+00:00',
  updatedAt: '2026-11-02T10:53:00+00:00',
}

/** `photos` — обязательное последнее поле обоих контрактов: без него карточка битая. */
describe('photos в карточке барбера и админской карточке', () => {
  it('isBarberProfile принимает пустое и заполненное портфолио', () => {
    expect(isBarberProfile(testBarberProfile)).toBe(true)
    expect(
      isBarberProfile({ ...testBarberProfile, photos: [PORTFOLIO_PHOTO] })
    ).toBe(true)
  })

  it('isBarberProfile отвергает карточку без photos или с битой фотографией', () => {
    const { photos, ...withoutPhotos } = testBarberProfile

    expect(photos).toStrictEqual([])
    expect(isBarberProfile(withoutPhotos)).toBe(false)
    expect(
      isBarberProfile({ ...testBarberProfile, photos: [PORTFOLIO_PHOTO, null] })
    ).toBe(false)
    expect(isBarberProfile({ ...testBarberProfile, photos: 'битое' })).toBe(false)
  })

  it('isAdminBarber принимает photos и отвергает карточку без них', () => {
    expect(isAdminBarber(ADMIN_BARBER_DETAIL)).toBe(true)
    expect(
      isAdminBarber({
        ...ADMIN_BARBER_DETAIL,
        photos: [{ ...PORTFOLIO_PHOTO, isBlocked: true, isPublished: true }],
      })
    ).toBe(true)

    const { photos, ...withoutPhotos } = ADMIN_BARBER_DETAIL

    expect(photos).toStrictEqual([])
    expect(isAdminBarber(withoutPhotos)).toBe(false)
    expect(
      isAdminBarber({
        ...ADMIN_BARBER_DETAIL,
        photos: [{ ...PORTFOLIO_PHOTO, url: '' }],
      })
    ).toBe(false)
  })
})

describe('isCreatedResource', () => {
  it('принимает непустой id и лишние поля', () => {
    expect(isCreatedResource({ id: 'photo-1' })).toBe(true)
    expect(isCreatedResource({ id: 'photo-1', url: '/x' })).toBe(true)
  })

  it('отвергает пустой id, не-объект и массив', () => {
    expect(isCreatedResource({ id: '' })).toBe(false)
    expect(isCreatedResource({})).toBe(false)
    expect(isCreatedResource(null)).toBe(false)
    expect(isCreatedResource([{ id: 'photo-1' }])).toBe(false)
  })
})

/** Ответ загрузки аватара: относительный URL раздачи и статус премодерации. */
describe('isAvatarUploadResponse', () => {
  const AVATAR_URL =
    '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

  it('принимает URL раздачи и статус, лишние поля игнорирует', () => {
    expect(
      isAvatarUploadResponse({
        avatarUrl: AVATAR_URL,
        moderationStatus: 'Pending',
      })
    ).toBe(true)
    expect(
      isAvatarUploadResponse({
        avatarUrl: AVATAR_URL,
        moderationStatus: 'Pending',
        extra: 1,
      })
    ).toBe(true)
  })

  it('отвергает пустой, чужой префикс, не-строку и не-объект', () => {
    expect(
      isAvatarUploadResponse({ avatarUrl: '', moderationStatus: 'Pending' })
    ).toBe(false)
    expect(
      isAvatarUploadResponse({
        avatarUrl: '/api/media/portfolio/x.jpg',
        moderationStatus: 'Pending',
      })
    ).toBe(false)
    expect(
      isAvatarUploadResponse({
        avatarUrl: 'https://example.com/avatar.jpg',
        moderationStatus: 'Pending',
      })
    ).toBe(false)
    expect(
      isAvatarUploadResponse({ avatarUrl: 1, moderationStatus: 'Pending' })
    ).toBe(false)
    expect(isAvatarUploadResponse({})).toBe(false)
    expect(isAvatarUploadResponse(null)).toBe(false)
    expect(isAvatarUploadResponse([])).toBe(false)
  })

  it('требует известный moderationStatus: без него шапка не знает, что рендерить', () => {
    expect(isAvatarUploadResponse({ avatarUrl: AVATAR_URL })).toBe(false)
    expect(
      isAvatarUploadResponse({
        avatarUrl: AVATAR_URL,
        moderationStatus: 'Queued',
      })
    ).toBe(false)
    expect(
      isAvatarUploadResponse({
        avatarUrl: AVATAR_URL,
        moderationStatus: null,
      })
    ).toBe(false)
  })
})

/** `BarberProfile.avatarUrl` — новое nullable-поле публичной карточки (#85). */
describe('avatarUrl в публичной карточке барбера', () => {
  it('принимает null и относительный URL', () => {
    expect(isBarberProfile(testBarberProfile)).toBe(true)
    expect(
      isBarberProfile({
        ...testBarberProfile,
        avatarUrl:
          '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg',
      })
    ).toBe(true)
  })

  it('отвергает карточку без avatarUrl и с нестроковым значением', () => {
    const { avatarUrl, ...withoutAvatar } = testBarberProfile

    expect(avatarUrl).toBeNull()
    expect(isBarberProfile(withoutAvatar)).toBe(false)
    expect(isBarberProfile({ ...testBarberProfile, avatarUrl: 1 })).toBe(false)
  })
})

const BOOKING_AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000115/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

const ADMIN_BOOKING = {
  id: '88888888-8888-4888-8888-888888888888',
  status: 'Confirmed',
  startAt: '2026-12-01T10:00:00Z',
  endAt: '2026-12-01T10:30:00Z',
  price: 45,
  customerNote: null,
  barberNote: null,
  cancellationReason: null,
  createdAt: '2026-11-01T10:00:00Z',
  customer: {
    id: null,
    firstName: 'Гость',
    lastName: '',
    email: null,
    phoneNumber: '+79990000000',
    avatarUrl: null,
  },
  barber: {
    profileId: '99999999-9999-4999-8999-999999999999',
    displayName: 'Тимур Барберов',
    avatarUrl: null,
  },
  service: { name: 'Стрижка' },
  salon: {
    id: '66666666-6666-4666-8666-666666666666',
    name: 'Салон',
    address: 'ул. Тестовая, 1',
    city: 'Москва',
    timeZoneId: 'Europe/Moscow',
  },
}

/**
 * `avatarUrl` — обязательное nullable-поле трёх чтений #86: выдачи поиска,
 * броней барбера и админских броней. Пропущенное поле роняет guard, потому что
 * сервер обещает его всегда, а карточка иначе молча теряет фото.
 */
describe('avatarUrl в guard-ах поиска и броней', () => {
  it('принимает avatarUrl: null во всех трёх группах', () => {
    expect(isBarberSearchItem(SEARCH_ITEM)).toBe(true)
    expect(isBarberBooking(BARBER_BOOKING)).toBe(true)
    expect(isAdminBooking(ADMIN_BOOKING)).toBe(true)
  })

  it('принимает строку avatarUrl во всех трёх группах', () => {
    expect(
      isBarberSearchItem({ ...SEARCH_ITEM, avatarUrl: BOOKING_AVATAR_URL })
    ).toBe(true)
    expect(
      isBarberBooking({
        ...BARBER_BOOKING,
        customer: {
          ...BARBER_BOOKING.customer,
          avatarUrl: BOOKING_AVATAR_URL,
        },
      })
    ).toBe(true)
    expect(
      isAdminBooking({
        ...ADMIN_BOOKING,
        customer: { ...ADMIN_BOOKING.customer, avatarUrl: BOOKING_AVATAR_URL },
        barber: { ...ADMIN_BOOKING.barber, avatarUrl: BOOKING_AVATAR_URL },
      })
    ).toBe(true)
  })

  it('отвергает отсутствие avatarUrl во всех трёх группах', () => {
    const { avatarUrl: searchAvatar, ...searchWithoutAvatar } = SEARCH_ITEM
    const { avatarUrl: customerAvatar, ...customerWithoutAvatar } =
      BARBER_BOOKING.customer
    const { avatarUrl: adminCustomerAvatar, ...adminCustomerWithoutAvatar } =
      ADMIN_BOOKING.customer
    const { avatarUrl: adminBarberAvatar, ...adminBarberWithoutAvatar } =
      ADMIN_BOOKING.barber

    expect(searchAvatar).toBeNull()
    expect(customerAvatar).toBeNull()
    expect(adminCustomerAvatar).toBeNull()
    expect(adminBarberAvatar).toBeNull()
    expect(isBarberSearchItem(searchWithoutAvatar)).toBe(false)
    expect(
      isBarberBooking({
        ...BARBER_BOOKING,
        customer: customerWithoutAvatar,
      })
    ).toBe(false)
    expect(
      isAdminBooking({
        ...ADMIN_BOOKING,
        customer: adminCustomerWithoutAvatar,
      })
    ).toBe(false)
    expect(
      isAdminBooking({
        ...ADMIN_BOOKING,
        barber: adminBarberWithoutAvatar,
      })
    ).toBe(false)
  })

  it('отвергает нестроковый avatarUrl во всех трёх группах', () => {
    expect(isBarberSearchItem({ ...SEARCH_ITEM, avatarUrl: 1 })).toBe(false)
    expect(
      isBarberBooking({
        ...BARBER_BOOKING,
        customer: { ...BARBER_BOOKING.customer, avatarUrl: {} },
      })
    ).toBe(false)
    expect(
      isAdminBooking({
        ...ADMIN_BOOKING,
        customer: { ...ADMIN_BOOKING.customer, avatarUrl: 42 },
      })
    ).toBe(false)
    expect(
      isAdminBooking({
        ...ADMIN_BOOKING,
        barber: { ...ADMIN_BOOKING.barber, avatarUrl: false },
      })
    ).toBe(false)
  })
})

const PROFILE = {
  id: '9e000000-0000-4000-8000-000000000003',
  email: 'eva@barbersearch.test',
  firstName: 'Ева',
  lastName: 'Клиентова',
  phoneNumber: null,
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  avatarRejectionReason: null,
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:02:00+00:00',
  lastLoginAt: null,
}

const ADMIN_USER_ROW = {
  id: '9e000000-0000-4000-8000-000000000003',
  email: 'eva@barbersearch.test',
  firstName: 'Ева',
  lastName: 'Клиентова',
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  role: 'Customer',
  isActive: true,
  isEmailVerified: true,
  isPhoneVerified: false,
  barberProfileId: null,
  lastLoginAt: null,
  createdAt: '2026-09-01T10:02:00+00:00',
}

const ADMIN_BARBER_ROW = {
  id: '9e800000-0000-4000-8000-000000000001',
  displayName: 'Артём Соколов',
  email: 'artem@barbersearch.test',
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  grade: 'ArtDirector',
  rating: null,
  reviewsCount: 0,
  isActive: true,
  isVerified: false,
  createdAt: '2026-09-28T15:12:00+00:00',
  updatedAt: '2026-11-02T10:53:00+00:00',
}

/**
 * `isAvatarBlocked` — обязательный флаг всех пяти контрактов #87, а `avatarUrl`
 * у админских строк — сырой URL владельца. Забытое поле роняет guard: шапка,
 * кабинет и админские карточки иначе молча показывали бы скрытое фото.
 */
describe('avatarUrl и isAvatarBlocked в контрактах #87', () => {
  const CONTRACTS = [
    ['MyProfile', isMyProfile, PROFILE],
    ['AdminUserRow', isAdminUserRow, ADMIN_USER_ROW],
    ['AdminUserDetail', isAdminUser, DETAIL],
    ['AdminBarberRow', isAdminBarberRow, ADMIN_BARBER_ROW],
    ['AdminBarberDetail', isAdminBarber, ADMIN_BARBER_DETAIL],
  ] as const

  it('принимает новый флаг во всех пяти контрактах', () => {
    for (const [, guard, value] of CONTRACTS) {
      expect(guard(value)).toBe(true)
    }
  })

  it('отвергает отсутствие isAvatarBlocked в каждом контракте', () => {
    for (const [name, guard, value] of CONTRACTS) {
      const { isAvatarBlocked, ...withoutFlag } = value as Record<string, unknown>

      expect(isAvatarBlocked, name).toBe(false)
      expect(guard({ ...withoutFlag, extra: name }), name).toBe(false)
    }
  })

  it('отвергает небулев isAvatarBlocked в каждом контракте', () => {
    for (const [name, guard, value] of CONTRACTS) {
      expect(guard({ ...value, isAvatarBlocked: 'true' }), name).toBe(false)
    }
  })

  it('принимает строку avatarUrl у админских строк и карточек', () => {
    const avatarUrl =
      '/api/media/avatars/9e000000-0000-4000-8000-000000000003/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

    expect(isAdminUserRow({ ...ADMIN_USER_ROW, avatarUrl })).toBe(true)
    expect(isAdminUser({ ...DETAIL, avatarUrl })).toBe(true)
    expect(isAdminBarberRow({ ...ADMIN_BARBER_ROW, avatarUrl })).toBe(true)
    expect(
      isAdminBarber({ ...ADMIN_BARBER_DETAIL, avatarUrl, isAvatarBlocked: true })
    ).toBe(true)
  })

  it('отвергает нестроковый avatarUrl админских строк', () => {
    expect(isAdminUserRow({ ...ADMIN_USER_ROW, avatarUrl: 1 })).toBe(false)
    expect(isAdminBarberRow({ ...ADMIN_BARBER_ROW, avatarUrl: {} })).toBe(false)
  })
})

/**
 * Премодерация фото (#89): статус и причина — обязательные поля всех контрактов,
 * где кадр или аватар отдаётся наружу. Пропущенное поле роняет guard: иначе
 * владелец не увидел бы «На проверке»/«Отклонено», а админ — причину отказа.
 */
describe('поля премодерации в контрактах #89', () => {
  const ADMIN_SUMMARY = {
    reviews: { pendingCount: 0, oldest: null },
    barbers: { unverifiedCount: 0 },
    salons: { inactiveCount: 0 },
    photos: { pendingAvatarsCount: 2, pendingPortfolioCount: 5 },
  }

  const ADMIN_AVATAR_PHOTO = {
    userId: '9e000000-0000-4000-8000-000000000003',
    firstName: 'Ева',
    lastName: 'Клиентова',
    email: 'eva@barbersearch.test',
    avatarUrl:
      '/api/media/avatars/9e000000-0000-4000-8000-000000000003/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg',
    moderationStatus: 'Pending',
    submittedAt: '2026-11-11T10:00:00Z',
  }

  const ADMIN_PORTFOLIO_PHOTO = {
    photoId: '9e200000-0000-4000-8000-000000000001',
    barberProfileId: '9e100000-0000-4000-8000-000000000015',
    barberDisplayName: 'Артём Соколов',
    ownerEmail: 'artem@barbersearch.test',
    url: '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/9e200000000040008000000000000001.jpg',
    description: null,
    isPublished: true,
    moderationStatus: 'Pending',
    submittedAt: '2026-11-11T10:00:00Z',
  }

  it('принимает новые обязательные поля во всех контрактах', () => {
    expect(isPortfolioPhoto(PORTFOLIO_PHOTO)).toBe(true)
    expect(isMyProfile(PROFILE)).toBe(true)
    expect(isAdminUserRow(ADMIN_USER_ROW)).toBe(true)
    expect(isAdminUser(DETAIL)).toBe(true)
    expect(isAdminBarberRow(ADMIN_BARBER_ROW)).toBe(true)
    expect(isAdminBarber(ADMIN_BARBER_DETAIL)).toBe(true)
    expect(isAdminSummary(ADMIN_SUMMARY)).toBe(true)
    expect(isAdminAvatarPhoto(ADMIN_AVATAR_PHOTO)).toBe(true)
    expect(isAdminPortfolioPhoto(ADMIN_PORTFOLIO_PHOTO)).toBe(true)
  })

  it('отвергает портфолио без moderationStatus или rejectionReason', () => {
    const { moderationStatus, ...withoutStatus } = PORTFOLIO_PHOTO
    const { rejectionReason, ...withoutReason } = PORTFOLIO_PHOTO

    expect(moderationStatus).toBe('Approved')
    expect(rejectionReason).toBeNull()
    expect(isPortfolioPhoto(withoutStatus)).toBe(false)
    expect(isPortfolioPhoto(withoutReason)).toBe(false)
    expect(
      isPortfolioPhoto({ ...PORTFOLIO_PHOTO, moderationStatus: 'Queued' })
    ).toBe(false)
    expect(
      isPortfolioPhoto({ ...PORTFOLIO_PHOTO, rejectionReason: 42 })
    ).toBe(false)
  })

  it('публичный ответ портфолио несёт Approved и null', () => {
    expect(
      isPortfolioPhoto({
        ...PORTFOLIO_PHOTO,
        moderationStatus: 'Approved',
        rejectionReason: null,
      })
    ).toBe(true)
  })

  it('отвергает профиль кабинета без статуса или причины аватара', () => {
    const { avatarModerationStatus, ...withoutStatus } = PROFILE
    const { avatarRejectionReason, ...withoutReason } = PROFILE

    expect(avatarModerationStatus).toBe('Approved')
    expect(avatarRejectionReason).toBeNull()
    expect(isMyProfile(withoutStatus)).toBe(false)
    expect(isMyProfile(withoutReason)).toBe(false)
    expect(
      isMyProfile({ ...PROFILE, avatarModerationStatus: 'Queued' })
    ).toBe(false)
    expect(
      isMyProfile({ ...PROFILE, avatarRejectionReason: { text: 'нет' } })
    ).toBe(false)
  })

  it('админская строка пользователя требует статус, карточка — ещё и причину', () => {
    const { avatarModerationStatus, ...rowWithoutStatus } = ADMIN_USER_ROW
    const { avatarModerationStatus: _detailStatus, ...detailWithoutStatus } =
      DETAIL
    const { avatarRejectionReason, ...detailWithoutReason } = DETAIL

    expect(avatarModerationStatus).toBe('Approved')
    expect(_detailStatus).toBe('Approved')
    expect(avatarRejectionReason).toBeNull()
    expect(isAdminUserRow(rowWithoutStatus)).toBe(false)
    expect(isAdminUser(detailWithoutStatus)).toBe(false)
    expect(isAdminUser(detailWithoutReason)).toBe(false)
  })

  it('админская строка барбера требует статус, карточка — ещё и причину', () => {
    const { avatarModerationStatus, ...rowWithoutStatus } = ADMIN_BARBER_ROW
    const { avatarModerationStatus: _detailStatus, ...detailWithoutStatus } =
      ADMIN_BARBER_DETAIL
    const { avatarRejectionReason, ...detailWithoutReason } = ADMIN_BARBER_DETAIL

    expect(avatarModerationStatus).toBe('Approved')
    expect(_detailStatus).toBe('Approved')
    expect(avatarRejectionReason).toBeNull()
    expect(isAdminBarberRow(rowWithoutStatus)).toBe(false)
    expect(isAdminBarber(detailWithoutStatus)).toBe(false)
    expect(isAdminBarber(detailWithoutReason)).toBe(false)
  })

  it('сводка требует блок photos с двумя неотрицательными целыми', () => {
    const { photos, ...withoutPhotos } = ADMIN_SUMMARY

    expect(photos).toStrictEqual({
      pendingAvatarsCount: 2,
      pendingPortfolioCount: 5,
    })
    expect(isAdminSummary(withoutPhotos)).toBe(false)
    expect(
      isAdminSummary({
        ...ADMIN_SUMMARY,
        photos: { pendingAvatarsCount: -1, pendingPortfolioCount: 0 },
      })
    ).toBe(false)
    expect(
      isAdminSummary({
        ...ADMIN_SUMMARY,
        photos: { pendingAvatarsCount: 0, pendingPortfolioCount: 1.5 },
      })
    ).toBe(false)
    expect(
      isAdminSummary({
        ...ADMIN_SUMMARY,
        photos: { pendingAvatarsCount: 0 },
      })
    ).toBe(false)
  })

  it('очередь аватаров отвергает строку без статуса или URL', () => {
    const { moderationStatus, ...withoutStatus } = ADMIN_AVATAR_PHOTO
    const { avatarUrl, ...withoutUrl } = ADMIN_AVATAR_PHOTO

    expect(moderationStatus).toBe('Pending')
    expect(avatarUrl).toContain('/api/media/avatars/')
    expect(isAdminAvatarPhoto(withoutStatus)).toBe(false)
    expect(isAdminAvatarPhoto(withoutUrl)).toBe(false)
    expect(
      isAdminAvatarPhoto({ ...ADMIN_AVATAR_PHOTO, moderationStatus: 'Queued' })
    ).toBe(false)
    expect(
      isAdminAvatarPhoto({ ...ADMIN_AVATAR_PHOTO, submittedAt: 1 })
    ).toBe(false)
  })

  it('очередь портфолио отвергает строку без статуса или с неверным isPublished', () => {
    const { moderationStatus, ...withoutStatus } = ADMIN_PORTFOLIO_PHOTO
    const { isPublished, ...withoutPublished } = ADMIN_PORTFOLIO_PHOTO

    expect(moderationStatus).toBe('Pending')
    expect(isPublished).toBe(true)
    expect(isAdminPortfolioPhoto(withoutStatus)).toBe(false)
    expect(isAdminPortfolioPhoto(withoutPublished)).toBe(false)
    expect(
      isAdminPortfolioPhoto({
        ...ADMIN_PORTFOLIO_PHOTO,
        moderationStatus: 'Queued',
      })
    ).toBe(false)
    expect(
      isAdminPortfolioPhoto({ ...ADMIN_PORTFOLIO_PHOTO, description: 42 })
    ).toBe(false)
  })
})

/**
 * Новый тип сущности не требует правки guard'а: `entityType` — непустая строка,
 * а не закрытый список. Тест фиксирует это обещание, чтобы «девятый тип» не
 * превратился в битый ответ на первом же фото.
 */
describe('isAdminAuditPage, тип сущности portfolio_photo', () => {
  const PAGE = {
    items: [
      {
        id: '9e700000-0000-4000-8000-000000000026',
        occurredAt: '2026-09-10T10:17:00+00:00',
        action: 'portfolio_photo.created',
        entityType: 'portfolio_photo',
        entityId: '9e200000-0000-4000-8000-000000000001',
        actor: null,
        changes: [
          {
            field: 'url',
            from: null,
            to: '/api/media/portfolio/9e100000-0000-4000-8000-000000000015/0123456789abcdef0123456789abcdef.jpg',
          },
          { field: 'sortOrder', from: null, to: 1 },
        ],
      },
    ],
    nextCursor: null,
  }

  it('принимает страницу с entityType portfolio_photo', () => {
    expect(isAdminAuditPage(PAGE)).toBe(true)
  })

  it('отвергает строку без типа и не-скалярный diff', () => {
    expect(
      isAdminAuditPage({
        ...PAGE,
        items: [{ ...PAGE.items[0], entityType: '' }],
      })
    ).toBe(false)
    expect(
      isAdminAuditPage({
        ...PAGE,
        items: [
          {
            ...PAGE.items[0],
            changes: [{ field: 'url', from: {}, to: null }],
          },
        ],
      })
    ).toBe(false)
  })
})
