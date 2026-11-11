import { describe, expect, it } from 'vitest'

import {
  isAdminBarber,
  isAdminSalonRow,
  isAdminUser,
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

/** Ответ загрузки аватара: только относительный URL раздачи, с непустым значением. */
describe('isAvatarUploadResponse', () => {
  const AVATAR_URL =
    '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

  it('принимает URL раздачи и игнорирует лишние поля', () => {
    expect(isAvatarUploadResponse({ avatarUrl: AVATAR_URL })).toBe(true)
    expect(isAvatarUploadResponse({ avatarUrl: AVATAR_URL, extra: 1 })).toBe(
      true
    )
  })

  it('отвергает пустой, чужой префикс, не-строку и не-объект', () => {
    expect(isAvatarUploadResponse({ avatarUrl: '' })).toBe(false)
    expect(
      isAvatarUploadResponse({ avatarUrl: '/api/media/portfolio/x.jpg' })
    ).toBe(false)
    expect(
      isAvatarUploadResponse({ avatarUrl: 'https://example.com/avatar.jpg' })
    ).toBe(false)
    expect(isAvatarUploadResponse({ avatarUrl: 1 })).toBe(false)
    expect(isAvatarUploadResponse({})).toBe(false)
    expect(isAvatarUploadResponse(null)).toBe(false)
    expect(isAvatarUploadResponse([])).toBe(false)
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
