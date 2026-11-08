import type {
  AdminBarber,
  AdminBarberDetail,
  AdminBarberReadiness,
} from '@/lib/admin-barbers'
import type {
  AdminBooking,
  AdminBookingBarber,
  AdminBookingCustomer,
  AdminBookingSalon,
  AdminBookingService,
} from '@/lib/admin-bookings'
import type { AdminReview } from '@/lib/admin-reviews'
import type {
  AdminBarberRequest,
  AdminBarberRequestApplicant,
} from '@/lib/admin-barber-requests'
import type {
  AdminSummary,
  AdminSummaryOldestReview,
} from '@/lib/admin-summary'
import type {
  AdminStatistics,
  AdminStatisticsBarberTopRow,
  AdminStatisticsCityTopRow,
  AdminStatisticsModeration,
  AdminStatisticsServiceTopRow,
} from '@/lib/admin-statistics'
import type {
  AdminAuditActor,
  AdminAuditChange,
  AdminAuditEntry,
  AdminAuditScalar,
} from '@/lib/admin-audit'
import type {
  AdminHairService,
  AdminSalon,
  AdminSpecialization,
} from '@/lib/admin-catalogue'
import type {
  AdminUserBarberProfile,
  AdminUserDetail,
  AdminUserRole,
  AdminUserRow,
} from '@/lib/admin-users'
import type { SessionUser } from '@/lib/auth'
import type {
  BarberRequest,
  BarberRequestStatus,
  MyBarberRequestResponse,
} from '@/lib/barber-requests'
import type { BarberBooking } from '@/lib/barber-bookings'
import type { BarberBookingOptions } from '@/lib/barber-booking-options'
import type {
  BarberNotification,
  BarberNotificationDelivery,
  BarberNotificationKind,
  NotificationLeadMinutes,
  NotificationSettings,
  TelegramLink,
} from '@/lib/barber-notifications'
import type { BarberClient } from '@/lib/barber-clients'
import type { BarberGrade } from '@/lib/barber-grade'
import type { DayOfWeek, Language, PaymentMethod, ServiceCategory } from '@/lib/barber-profile-labels'
import type {
  BarberSchedule,
  BarberScheduleDay,
  BarberScheduleException,
  BarberScheduleSalon,
  BarberScheduleTimeOff,
} from '@/lib/barber-schedule'
import type { BarberSearchItem } from '@/lib/barber-search'
import type { BarberSalonSummary } from '@/lib/barber-salons'
import type { BarberSlot, BarberSlots } from '@/lib/barber-slots'
import type { SalonBarber } from '@/lib/salon-barbers'
import type { Booking, BookingStatus } from '@/lib/bookings'
import type {
  BarberProfile,
  BarberProfileSalon,
  BarberProfileService,
  BarberProfileSpecialization,
  WorkingSchedule,
} from '@/lib/catalog'
import type { HairServiceOption } from '@/lib/hair-services'
import type { MyReview } from '@/lib/my-reviews'
import type { MyProfile } from '@/lib/my-profile'
import type { Page } from '@/lib/paging'
import { isPlaceKind } from '@/lib/place-kind'
import type { Review } from '@/lib/reviews'
import { isSalonModerationStatus } from '@/lib/salon-moderation'
import type { SalonDirectoryItem } from '@/lib/salons'
import type { SpecializationOption } from '@/lib/specializations'

/**
 * Ручные guard'ы успешных ответов API.
 *
 * Проверяем только то, что читает UI, и не разбираем формат дат и времени: строка
 * остаётся строкой. Лишние поля сервера игнорируются — guard не должен ломаться
 * от расширения контракта. Импортируются только типы, поэтому рантайм-циклов нет.
 */

type Guard<T> = (value: unknown) => value is T

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isString(value: unknown): value is string {
  return typeof value === 'string'
}

/** Идентификаторы и прочие обязательные строки: пустая строка — тоже битый ответ. */
function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value !== ''
}

function isBoolean(value: unknown): value is boolean {
  return typeof value === 'boolean'
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

/**
 * Счётчик очереди: целое, не меньше нуля.
 *
 * `-1`, `1.5` и `NaN` — битый ответ, а не «почти число»: сводка обещает количество строк,
 * и дробное значение скрыло бы нарушение контракта.
 */
function isNonNegativeInteger(value: unknown): value is number {
  return (
    typeof value === 'number'
    && Number.isSafeInteger(value)
    && value >= 0
  )
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isArrayOf<T>(guard: Guard<T>): Guard<T[]> {
  return (value): value is T[] => Array.isArray(value) && value.every(guard)
}

function isNullable<T>(guard: Guard<T>): Guard<T | null> {
  return (value): value is T | null => value === null || guard(value)
}

function isOneOf<T extends string>(values: readonly T[]): Guard<T> {
  return (value): value is T =>
    typeof value === 'string' && values.includes(value as T)
}

function isPageOf<T>(guard: Guard<T>): Guard<Page<T>> {
  return (value): value is Page<T> =>
    isRecord(value) &&
    isArrayOf(guard)(value.items) &&
    isNullableString(value.nextCursor)
}

const BOOKING_STATUSES: readonly BookingStatus[] = [
  'Pending',
  'Confirmed',
  'Cancelled',
  'Completed',
  'NoShow',
]

const BARBER_GRADES: readonly BarberGrade[] = [
  'None',
  'Intern',
  'JuniorBarber',
  'MiddleBarber',
  'SeniorBarber',
  'ArtDirector',
  'BarberSchoolTrainer',
  'Manager',
  'CompetitionBarber',
  'BrandAmbassador',
  'BarbershopOwner',
]

const LANGUAGES: readonly Language[] = ['Ru', 'En', 'De', 'Kg', 'Tj', 'Uz']

const PAYMENT_METHODS: readonly PaymentMethod[] = [
  'Cash',
  'Card',
  'DigitalWallet',
  'Sbp',
  'BankTransfer',
]

const SERVICE_CATEGORIES: readonly ServiceCategory[] = [
  'Haircut',
  'Beard',
  'Coloring',
  'Styling',
  'HairTreatment',
  'Other',
]

const DAYS_OF_WEEK: readonly DayOfWeek[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
]

const USER_ROLES: readonly AdminUserRole[] = ['Customer', 'Barber', 'Admin']

const BARBER_REQUEST_STATUSES: readonly BarberRequestStatus[] = [
  'Pending',
  'Approved',
  'Rejected',
]

const BARBER_NOTIFICATION_KINDS: readonly BarberNotificationKind[] = [
  'Upcoming',
  'Digest',
]

const BARBER_NOTIFICATION_DELIVERIES: readonly BarberNotificationDelivery[] = [
  'Pending',
  'Sent',
  'Skipped',
  'Failed',
]

const NOTIFICATION_LEAD_MINUTES: readonly NotificationLeadMinutes[] = [
  15, 30, 60,
]

/** `HH:mm`, а не `TimeOnly`-сериализация `08:00:00`: так значение уже готово для input. */
const DIGEST_TIME_PATTERN = /^(?:[01]\d|2[0-3]):[0-5]\d$/

const isBookingStatus = isOneOf(BOOKING_STATUSES)
const isBarberGrade = isOneOf(BARBER_GRADES)
const isLanguage = isOneOf(LANGUAGES)
const isPaymentMethod = isOneOf(PAYMENT_METHODS)
const isServiceCategory = isOneOf(SERVICE_CATEGORIES)
const isDayOfWeek = isOneOf(DAYS_OF_WEEK)
const isUserRole = isOneOf(USER_ROLES)
const isBarberRequestStatus = isOneOf(BARBER_REQUEST_STATUSES)
const isBarberNotificationKind = isOneOf(BARBER_NOTIFICATION_KINDS)
const isBarberNotificationDelivery = isOneOf(BARBER_NOTIFICATION_DELIVERIES)

/** Только 15/30/60: домен не принимает другие значения, и селект их не предлагает. */
function isNotificationLeadMinutes(
  value: unknown
): value is NotificationLeadMinutes {
  return (
    typeof value === 'number' &&
    NOTIFICATION_LEAD_MINUTES.includes(value as NotificationLeadMinutes)
  )
}

/** `HH:mm` с ведущими нулями; `8:00` и `08:00:00` — битый контракт. */
function isDigestTime(value: unknown): value is string {
  return typeof value === 'string' && DIGEST_TIME_PATTERN.test(value)
}

/** `{ id }` после регистрации, создания брони, отзыва и пользователя. */
export function isCreatedId(value: unknown): value is { id: string } {
  return isRecord(value) && isNonEmptyString(value.id)
}

/** `{ token }` из `GET api/auth/csrf`. */
export function isCsrfToken(value: unknown): value is { token: string } {
  return isRecord(value) && isString(value.token)
}

export function isSessionUser(value: unknown): value is SessionUser {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.email) &&
    isString(value.role) &&
    (value.barberProfileId === null || isNonEmptyString(value.barberProfileId)) &&
    isBoolean(value.isEmailVerified)
  )
}

/**
 * Профиль кабинета: имя, телефон, роль и подтверждения.
 *
 * `phoneNumber`, `avatarUrl` и `lastLoginAt` контракт допускает `null`; остальные
 * строки обязательны. Даты не разбираются — как и в других guard'ах, строка остаётся
 * строкой.
 */
export function isMyProfile(value: unknown): value is MyProfile {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.email) &&
    isString(value.firstName) &&
    isString(value.lastName) &&
    isNullableString(value.phoneNumber) &&
    isNullableString(value.avatarUrl) &&
    isUserRole(value.role) &&
    (value.barberProfileId === null || isNonEmptyString(value.barberProfileId)) &&
    isBoolean(value.isEmailVerified) &&
    isBoolean(value.isPhoneVerified) &&
    isString(value.createdAt) &&
    isNullableString(value.lastLoginAt)
  )
}

export function isBooking(value: unknown): value is Booking {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    (value.userId === null || isNonEmptyString(value.userId)) &&
    isNonEmptyString(value.barberProfileId) &&
    isNonEmptyString(value.barberServiceId) &&
    isNonEmptyString(value.salonId) &&
    isString(value.startAt) &&
    isString(value.endAt) &&
    isNumber(value.price) &&
    isBookingStatus(value.status) &&
    isString(value.idempotencyKey) &&
    isNullableString(value.customerNote) &&
    isNullableString(value.barberNote) &&
    isNullableString(value.cancellationReason) &&
    isString(value.createdAt)
  )
}

export const isBookingPage = isPageOf(isBooking)

export function isMyReview(value: unknown): value is MyReview {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNumber(value.rating) &&
    isNullableString(value.text) &&
    isBoolean(value.isPublished) &&
    isString(value.createdAt) &&
    isString(value.updatedAt) &&
    isRecord(value.barber) &&
    isNonEmptyString(value.barber.id) &&
    isString(value.barber.displayName) &&
    isRecord(value.visit) &&
    isNonEmptyString(value.visit.bookingId) &&
    isString(value.visit.startAt) &&
    isString(value.visit.timeZoneId) &&
    isNumber(value.visit.price) &&
    isString(value.visit.serviceName) &&
    isString(value.visit.salonName)
  )
}

export const isMyReviewPage = isPageOf(isMyReview)

export function isBarberBooking(value: unknown): value is BarberBooking {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isBookingStatus(value.status) &&
    isString(value.startAt) &&
    isString(value.endAt) &&
    isNumber(value.price) &&
    isNullableString(value.customerNote) &&
    isNullableString(value.barberNote) &&
    isNullableString(value.cancellationReason) &&
    isRecord(value.customer) &&
    isString(value.customer.displayName) &&
    isNullableString(value.customer.phoneNumber) &&
    isRecord(value.service) &&
    isString(value.service.name) &&
    isRecord(value.salon) &&
    isString(value.salon.name) &&
    isString(value.salon.address) &&
    isString(value.salon.city) &&
    isString(value.salon.timeZoneId)
  )
}

export const isBarberBookingPage = isPageOf(isBarberBooking)

/** Строка подсказки клиента: телефон может быть не заполнен, email — нет. */
export function isBarberClient(value: unknown): value is BarberClient {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.firstName) &&
    isString(value.lastName) &&
    isString(value.email) &&
    isNullableString(value.phoneNumber)
  )
}

export const isBarberClientList = isArrayOf(isBarberClient)

export function isReview(value: unknown): value is Review {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.barberProfileId) &&
    isNonEmptyString(value.userId) &&
    isNonEmptyString(value.bookingId) &&
    isNumber(value.rating) &&
    isNullableString(value.text) &&
    isBoolean(value.isPublished) &&
    isString(value.createdAt) &&
    isString(value.updatedAt)
  )
}

export const isReviewPage = isPageOf(isReview)

export function isBarberSearchItem(value: unknown): value is BarberSearchItem {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.displayName) &&
    isBarberGrade(value.grade) &&
    isNumber(value.yearsOfExperience) &&
    isArrayOf(isLanguage)(value.languages) &&
    isNullable(isNumber)(value.rating) &&
    isNumber(value.reviewsCount) &&
    isBoolean(value.isVerified) &&
    isNullable(isNonEmptyString)(value.primarySalonId) &&
    isNullableString(value.primarySalonName) &&
    isNullable(isPlaceKind)(value.primaryPlaceKind) &&
    isNullableString(value.city) &&
    isArrayOf(isString)(value.additionalServices)
  )
}

export const isBarberSearchPage = isPageOf(isBarberSearchItem)

/**
 * Подсказки фильтра «дополнительные услуги»: массив непустых строк.
 *
 * Пустая строка в ответе — битый контракт: такой вариант попал бы в выпадающий
 * список и не дал бы отфильтровать ничего осмысленного.
 */
export const isAdditionalServiceList = isArrayOf(isNonEmptyString)

/** Строка публичного справочника специализаций; описание может отсутствовать. */
export function isSpecializationOption(
  value: unknown
): value is SpecializationOption {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isNullableString(value.description)
  )
}

export const isSpecializationList = isArrayOf(isSpecializationOption)

/** Строка публичного каталога салонов для выбора. */
export function isSalonDirectoryItem(
  value: unknown
): value is SalonDirectoryItem {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isString(value.city) &&
    isString(value.address) &&
    isString(value.timeZoneId) &&
    isPlaceKind(value.kind)
  )
}

export const isSalonDirectoryList = isArrayOf(isSalonDirectoryItem)

/** Привязанный салон в списке «Мои салоны». */
export function isBarberSalonSummary(
  value: unknown
): value is BarberSalonSummary {
  return (
    isRecord(value) &&
    isNonEmptyString(value.salonId) &&
    isString(value.name) &&
    isString(value.city) &&
    isString(value.address) &&
    isString(value.timeZoneId) &&
    isPlaceKind(value.kind) &&
    isBoolean(value.isListed) &&
    isBoolean(value.isPrimary) &&
    isString(value.country) &&
    isNullableString(value.postalCode) &&
    isNumber(value.latitude) &&
    isNumber(value.longitude) &&
    isNullableString(value.phoneNumber) &&
    isNullableString(value.websiteUrl) &&
    isNullable(isNonEmptyString)(value.ownerUserId) &&
    isSalonModerationStatus(value.moderationStatus) &&
    isNullableString(value.rejectionReason)
  )
}

export const isBarberSalonSummaryList = isArrayOf(isBarberSalonSummary)

/** Барбер в составе салона: профиль, владелец профиля, имя, email и два статуса. */
export function isSalonBarber(value: unknown): value is SalonBarber {
  return (
    isRecord(value) &&
    isNonEmptyString(value.barberProfileId) &&
    isNonEmptyString(value.userId) &&
    isString(value.displayName) &&
    isString(value.email) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isVerified)
  )
}

export const isSalonBarberList = isArrayOf(isSalonBarber)

/** Строка публичного справочника услуг; длительность и описание UI не читает. */
export function isHairServiceOption(value: unknown): value is HairServiceOption {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isServiceCategory(value.category)
  )
}

export const isHairServiceList = isArrayOf(isHairServiceOption)

function isWorkingSchedule(value: unknown): value is WorkingSchedule {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isDayOfWeek(value.dayOfWeek) &&
    isString(value.startTime) &&
    isString(value.endTime) &&
    isBoolean(value.isDayOff)
  )
}

function isProfileService(value: unknown): value is BarberProfileService {
  return (
    isRecord(value) &&
    isNonEmptyString(value.barberServiceId) &&
    isNonEmptyString(value.hairServiceId) &&
    isString(value.name) &&
    isServiceCategory(value.category) &&
    isNumber(value.defaultDurationMinutes) &&
    isNumber(value.price) &&
    isNumber(value.durationMinutes) &&
    isBoolean(value.isActive)
  )
}

function isProfileSalon(value: unknown): value is BarberProfileSalon {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.salonId) &&
    isString(value.name) &&
    isString(value.address) &&
    isString(value.city) &&
    isString(value.country) &&
    isString(value.timeZoneId) &&
    isPlaceKind(value.kind) &&
    isBoolean(value.isPrimary) &&
    isArrayOf(isWorkingSchedule)(value.workingSchedules)
  )
}

function isSpecialization(
  value: unknown
): value is BarberProfileSpecialization {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.specializationId) &&
    isString(value.name) &&
    isNullableString(value.description)
  )
}

export function isBarberProfile(value: unknown): value is BarberProfile {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.userId) &&
    isString(value.displayName) &&
    isBarberGrade(value.grade) &&
    isNullableString(value.bio) &&
    isNumber(value.yearsOfExperience) &&
    isArrayOf(isLanguage)(value.languages) &&
    isArrayOf(isString)(value.certificatesAndTraining) &&
    isNullable(isNumber)(value.rating) &&
    isNumber(value.reviewsCount) &&
    isArrayOf(isString)(value.achievements) &&
    isArrayOf(isPaymentMethod)(value.paymentMethods) &&
    isArrayOf(isString)(value.communicationFormat) &&
    isArrayOf(isString)(value.goodies) &&
    isArrayOf(isString)(value.additionalServices) &&
    isString(value.latePolicy) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isVerified) &&
    isArrayOf(isProfileService)(value.services) &&
    isArrayOf(isProfileSalon)(value.salons) &&
    isArrayOf(isSpecialization)(value.specializations)
  )
}

/**
 * Данные формы ручной записи: активные услуги и салоны собственного профиля.
 *
 * Формы вложенных объектов те же, что у публичной карточки; лишние поля
 * игнорируются, чтобы расширение контракта не ломало форму.
 */
export function isBarberBookingOptions(
  value: unknown
): value is BarberBookingOptions {
  return (
    isRecord(value) &&
    isArrayOf(isProfileService)(value.services) &&
    isArrayOf(isProfileSalon)(value.salons)
  )
}

function isScheduleDay(value: unknown): value is BarberScheduleDay {
  return (
    isRecord(value) &&
    isDayOfWeek(value.dayOfWeek) &&
    isBoolean(value.isDayOff) &&
    isString(value.startTime) &&
    isString(value.endTime)
  )
}

function isScheduleWeek(value: unknown): value is BarberScheduleDay[] {
  return (
    Array.isArray(value) &&
    value.length === 7 &&
    value.every(isScheduleDay)
  )
}

function isScheduleException(
  value: unknown
): value is BarberScheduleException {
  return (
    isRecord(value) &&
    isString(value.date) &&
    isBoolean(value.isDayOff) &&
    isString(value.startTime) &&
    isString(value.endTime) &&
    isNullableString(value.reason)
  )
}

function isScheduleSalon(value: unknown): value is BarberScheduleSalon {
  return (
    isRecord(value) &&
    isNonEmptyString(value.salonId) &&
    isString(value.name) &&
    isString(value.city) &&
    isString(value.timeZoneId) &&
    isBoolean(value.isPrimary) &&
    isScheduleWeek(value.weekly) &&
    isArrayOf(isScheduleException)(value.exceptions)
  )
}

function isScheduleTimeOff(
  value: unknown
): value is BarberScheduleTimeOff {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.startAt) &&
    isString(value.endAt) &&
    isNullableString(value.reason)
  )
}

/** Ответ `GET api/barber-profiles/{id}/schedule`. */
export function isBarberSchedule(value: unknown): value is BarberSchedule {
  return (
    isRecord(value) &&
    isNonEmptyString(value.barberProfileId) &&
    isArrayOf(isScheduleSalon)(value.salons) &&
    isArrayOf(isScheduleTimeOff)(value.timeOffs)
  )
}

function isBarberSlot(value: unknown): value is BarberSlot {
  return (
    isRecord(value) && isString(value.startAt) && isString(value.endAt)
  )
}

export function isBarberSlots(value: unknown): value is BarberSlots {
  return (
    isRecord(value) &&
    isNonEmptyString(value.barberProfileId) &&
    isNonEmptyString(value.salonId) &&
    isNullable(isNonEmptyString)(value.barberServiceId) &&
    isNullable(isNonEmptyString)(value.hairServiceId) &&
    isString(value.date) &&
    isString(value.timeZoneId) &&
    isString(value.dayStart) &&
    isString(value.dayEnd) &&
    isNumber(value.durationMinutes) &&
    isBoolean(value.isWorkingDay) &&
    isArrayOf(isBarberSlot)(value.slots)
  )
}

function isAdminReviewBarber(value: unknown): boolean {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.displayName) &&
    isNullable(isNumber)(value.rating) &&
    isNumber(value.reviewsCount) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isVerified)
  )
}

function isAdminReviewCustomer(value: unknown): boolean {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.fullName) &&
    isString(value.email)
  )
}

function isAdminReviewVisit(value: unknown): boolean {
  return (
    isRecord(value) &&
    isNonEmptyString(value.bookingId) &&
    isString(value.startAt) &&
    isNumber(value.price) &&
    isString(value.serviceName) &&
    isString(value.salonName) &&
    isString(value.timeZoneId)
  )
}

export function isAdminReview(value: unknown): value is AdminReview {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNumber(value.rating) &&
    isNullableString(value.text) &&
    isBoolean(value.isPublished) &&
    isString(value.createdAt) &&
    isString(value.updatedAt) &&
    isAdminReviewBarber(value.barber) &&
    isAdminReviewCustomer(value.customer) &&
    isAdminReviewVisit(value.visit)
  )
}

export const isAdminReviewPage = isPageOf(isAdminReview)

function isReadiness(value: unknown): value is AdminBarberReadiness {
  return (
    isRecord(value) &&
    isBoolean(value.isProfileFilled) &&
    isBoolean(value.isEmailVerified) &&
    isNumber(value.servicesCount) &&
    isNumber(value.salonsCount) &&
    isNumber(value.scheduleDaysCount) &&
    isNumber(value.photosCount)
  )
}

export function isAdminBarberRow(value: unknown): value is AdminBarber {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.displayName) &&
    isString(value.email) &&
    isBarberGrade(value.grade) &&
    isNullable(isNumber)(value.rating) &&
    isNumber(value.reviewsCount) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isVerified) &&
    isString(value.createdAt) &&
    isString(value.updatedAt)
  )
}

export function isAdminBarber(value: unknown): value is AdminBarberDetail {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.userId) &&
    isString(value.displayName) &&
    isString(value.email) &&
    isNullableString(value.phoneNumber) &&
    isNullableString(value.avatarUrl) &&
    isBarberGrade(value.grade) &&
    isNullableString(value.bio) &&
    isNumber(value.yearsOfExperience) &&
    isNullable(isNumber)(value.rating) &&
    isNumber(value.reviewsCount) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isVerified) &&
    isReadiness(value.readiness) &&
    isString(value.createdAt) &&
    isString(value.updatedAt)
  )
}

export const isAdminBarberPage = isPageOf(isAdminBarberRow)

function isAdminUserBarberProfile(
  value: unknown
): value is AdminUserBarberProfile {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.displayName) &&
    isBoolean(value.isVerified) &&
    isBoolean(value.isActive)
  )
}

export function isAdminUserRow(value: unknown): value is AdminUserRow {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.email) &&
    isString(value.firstName) &&
    isString(value.lastName) &&
    isUserRole(value.role) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isEmailVerified) &&
    isBoolean(value.isPhoneVerified) &&
    isNullable(isNonEmptyString)(value.barberProfileId) &&
    isNullableString(value.lastLoginAt) &&
    isString(value.createdAt)
  )
}

export function isAdminUser(value: unknown): value is AdminUserDetail {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.email) &&
    isString(value.firstName) &&
    isString(value.lastName) &&
    isNullableString(value.phoneNumber) &&
    isNullableString(value.avatarUrl) &&
    isUserRole(value.role) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isEmailVerified) &&
    isBoolean(value.isPhoneVerified) &&
    isNullable(isNonEmptyString)(value.barberProfileId) &&
    (value.barberProfile === null ||
      isAdminUserBarberProfile(value.barberProfile)) &&
    isNullableString(value.lastLoginAt) &&
    isNullableString(value.lockoutEnd) &&
    isNumber(value.failedLoginAttempts) &&
    isNullableString(value.consentPolicyVersion) &&
    isNullableString(value.consentedAt) &&
    isString(value.createdAt) &&
    isString(value.updatedAt)
  )
}

export const isAdminUserPage = isPageOf(isAdminUserRow)

/** Строка админского справочника услуг: полный DTO формы и счётчик барберов. */
export function isAdminHairServiceRow(value: unknown): value is AdminHairService {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isServiceCategory(value.category) &&
    isNumber(value.defaultDurationMinutes) &&
    isNullableString(value.description) &&
    isBoolean(value.isActive) &&
    isNumber(value.usageCount)
  )
}

export const isAdminHairServiceList = isArrayOf(isAdminHairServiceRow)

/** Строка админского справочника специализаций. */
export function isAdminSpecializationRow(
  value: unknown
): value is AdminSpecialization {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isNullableString(value.description) &&
    isBoolean(value.isActive) &&
    isNumber(value.usageCount)
  )
}

export const isAdminSpecializationList = isArrayOf(isAdminSpecializationRow)

/** Строка админского справочника салонов: все поля формы правки, модерация и владелец. */
export function isAdminSalonRow(value: unknown): value is AdminSalon {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isString(value.address) &&
    isString(value.city) &&
    isNullableString(value.postalCode) &&
    isString(value.country) &&
    isString(value.timeZoneId) &&
    isNumber(value.latitude) &&
    isNumber(value.longitude) &&
    isNullableString(value.phoneNumber) &&
    isNullableString(value.websiteUrl) &&
    isBoolean(value.isActive) &&
    isSalonModerationStatus(value.moderationStatus) &&
    isPlaceKind(value.kind) &&
    isBoolean(value.isListed) &&
    isNullableString(value.rejectionReason) &&
    isNullableString(value.reviewedAt) &&
    isNullableString(value.ownerEmail) &&
    isNumber(value.usageCount)
  )
}

export const isAdminSalonList = isArrayOf(isAdminSalonRow)

/**
 * Скаляр diff'а: `string | number | boolean`.
 *
 * Объект или массив здесь — битый ответ, а не «почти скаляр»: `changes` обещает
 * ровно JSON-скаляры, и молчаливое приведение скрыло бы нарушение контракта.
 */
function isAdminAuditScalar(value: unknown): value is AdminAuditScalar {
  return (
    typeof value === 'string'
    || typeof value === 'boolean'
    || isNumber(value)
  )
}

/** Автор записи: email может быть `null` (аккаунт удалён), id — нет. */
function isAdminAuditActor(value: unknown): value is AdminAuditActor {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNullableString(value.email)
  )
}

/** Изменение одного поля: `from`/`to` — скаляр либо `null`, оба поля обязательны. */
function isAdminAuditChange(value: unknown): value is AdminAuditChange {
  return (
    isRecord(value) &&
    isNonEmptyString(value.field) &&
    isNullable(isAdminAuditScalar)(value.from) &&
    isNullable(isAdminAuditScalar)(value.to)
  )
}

/**
 * Строка журнала: `actor: null` — системная запись, `changes` — массив изменений.
 *
 * Лишние поля игнорируются, чтобы расширение контракта не ломало чтение.
 */
export function isAdminAuditEntry(value: unknown): value is AdminAuditEntry {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.occurredAt) &&
    isNonEmptyString(value.action) &&
    isNonEmptyString(value.entityType) &&
    isNonEmptyString(value.entityId) &&
    (value.actor === null || isAdminAuditActor(value.actor)) &&
    isArrayOf(isAdminAuditChange)(value.changes)
  )
}

export const isAdminAuditPage = isPageOf(isAdminAuditEntry)

function isAdminBookingCustomer(value: unknown): value is AdminBookingCustomer {
  return (
    isRecord(value) &&
    (value.id === null || isNonEmptyString(value.id)) &&
    isString(value.firstName) &&
    isString(value.lastName) &&
    (value.email === null || isString(value.email)) &&
    isNullableString(value.phoneNumber)
  )
}

function isAdminBookingBarber(value: unknown): value is AdminBookingBarber {
  return (
    isRecord(value) &&
    isNonEmptyString(value.profileId) &&
    isString(value.displayName)
  )
}

function isAdminBookingService(value: unknown): value is AdminBookingService {
  return isRecord(value) && isString(value.name)
}

function isAdminBookingSalon(value: unknown): value is AdminBookingSalon {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.name) &&
    isString(value.address) &&
    isString(value.city) &&
    isString(value.timeZoneId)
  )
}

/**
 * Строка админских броней: полный контекст клиента, барбера, услуги и салона.
 *
 * Строгий guard: `status` — одно из пяти имён, `price` — число, обе заметки и
 * причина отмены — строка или `null`, вложенные объекты обязательны. Лишние поля игнорируются,
 * чтобы расширение контракта не ломало чтение. Email клиента — PII, но она
 * приходит только в этом админском ответе (§4.6).
 */
export function isAdminBooking(value: unknown): value is AdminBooking {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isBookingStatus(value.status) &&
    isString(value.startAt) &&
    isString(value.endAt) &&
    isNumber(value.price) &&
    isNullableString(value.customerNote) &&
    isNullableString(value.barberNote) &&
    isNullableString(value.cancellationReason) &&
    isString(value.createdAt) &&
    isAdminBookingCustomer(value.customer) &&
    isAdminBookingBarber(value.barber) &&
    isAdminBookingService(value.service) &&
    isAdminBookingSalon(value.salon)
  )
}

export const isAdminBookingPage = isPageOf(isAdminBooking)

/**
 * Самая старая строка очереди модерации: оценки 1..5, все строки обязательны.
 *
 * Email клиента в этом контракте нет — только имя; `barberDisplayName` и
 * `customerDisplayName` экран показывает как есть.
 */
function isAdminSummaryOldestReview(
  value: unknown
): value is AdminSummaryOldestReview {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    typeof value.rating === 'number' &&
    Number.isInteger(value.rating) &&
    value.rating >= 1 &&
    value.rating <= 5 &&
    isString(value.createdAt) &&
    isString(value.barberDisplayName) &&
    isString(value.customerDisplayName)
  )
}

/**
 * Тело админской сводки: три обязательных блока и неотрицательные целые счётчики.
 *
 * `oldest` — `null` (очередь пуста) или компактная строка. Лишние поля игнорируются:
 * `total`/`nextCursor` контракт не обещает, но и не запрещает; guard их не требует.
 */
export function isAdminSummary(value: unknown): value is AdminSummary {
  if (
    !isRecord(value) ||
    !isRecord(value.reviews) ||
    !isRecord(value.barbers) ||
    !isRecord(value.salons)
  ) {
    return false
  }

  const { reviews, barbers, salons } = value

  return (
    isNonNegativeInteger(reviews.pendingCount) &&
    (reviews.oldest === null || isAdminSummaryOldestReview(reviews.oldest)) &&
    isNonNegativeInteger(barbers.unverifiedCount) &&
    isNonNegativeInteger(salons.inactiveCount)
  )
}

function isPercentage(value: unknown): value is number {
  return isNumber(value) && value >= 0 && value <= 1
}

function isNullablePercentage(value: unknown): value is number | null {
  return value === null || isPercentage(value)
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || isNumber(value)
}

/** Дата контракта — только ISO-день: время и локальные форматы сюда не проходят. */
function isStatisticsDay(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
}

function isTopOther(value: unknown): boolean {
  return (
    isRecord(value) &&
    isNonNegativeInteger(value.visits) &&
    isNumber(value.turnover) &&
    value.turnover >= 0
  )
}

function isBarberTopRow(value: unknown): value is AdminStatisticsBarberTopRow {
  return (
    isRecord(value) &&
    isNonEmptyString(value.barberProfileId) &&
    isString(value.displayName) &&
    isNonNegativeInteger(value.visits) &&
    isNumber(value.turnover) &&
    isNumber(value.averageCheck) &&
    isNonNegativeInteger(value.noShow) &&
    (value.rating === null || isNumber(value.rating)) &&
    isNonNegativeInteger(value.reviewsCount)
  )
}

function isServiceTopRow(value: unknown): value is AdminStatisticsServiceTopRow {
  return (
    isRecord(value) &&
    isNonEmptyString(value.hairServiceId) &&
    isString(value.name) &&
    isNonNegativeInteger(value.visits) &&
    isNumber(value.turnover) &&
    isNumber(value.averageCheck)
  )
}

function isCityTopRow(value: unknown): value is AdminStatisticsCityTopRow {
  return (
    isRecord(value) &&
    isString(value.name) &&
    isNonNegativeInteger(value.visits) &&
    isNumber(value.turnover) &&
    isNumber(value.averageCheck) &&
    isNonNegativeInteger(value.activeBarbers)
  )
}

function isModeration(value: unknown): value is AdminStatisticsModeration {
  return (
    isRecord(value) &&
    isNonNegativeInteger(value.submitted) &&
    isNonNegativeInteger(value.approved) &&
    isNonNegativeInteger(value.rejected) &&
    isNullablePercentage(value.approvalRate) &&
    isNullableNumber(value.medianDecisionHours)
  )
}

/**
 * Тело админской статистики: все девять секций контракта.
 *
 * Guard строгий в том, что читает экран: обязательные поля, конечные числа,
 * доли `0..1`, пять счётчиков распределения и длина дневного ряда, равная
 * периоду. Лишние поля сервера игнорируются, поэтому расширение контракта
 * не ломает экран; битое тело — ошибка экрана, а не «почти числа».
 */
export function isAdminStatistics(value: unknown): value is AdminStatistics {
  if (
    !isRecord(value) ||
    !isRecord(value.period) ||
    !isRecord(value.overview) ||
    !isRecord(value.series) ||
    !isRecord(value.statuses) ||
    !isRecord(value.clients) ||
    !isRecord(value.supply) ||
    !isRecord(value.top) ||
    !isRecord(value.quality)
  ) {
    return false
  }

  const { period, overview, series, statuses, clients, supply, top, quality } =
    value

  if (
    !isStatisticsDay(period.from) ||
    !isStatisticsDay(period.to) ||
    !isNonNegativeInteger(period.days) ||
    period.days < 1 ||
    !isNonEmptyString(period.timeZone) ||
    !isString(value.generatedAt)
  ) {
    return false
  }

  if (
    period.previous !== null &&
    (!isRecord(period.previous) ||
      !isStatisticsDay(period.previous.from) ||
      !isStatisticsDay(period.previous.to))
  ) {
    return false
  }

  if (
    !isRecord(overview.completedVisits) ||
    !isNonNegativeInteger(overview.completedVisits.value) ||
    !isNullableNumber(overview.completedVisits.previous) ||
    !isRecord(overview.turnover) ||
    !isNumber(overview.turnover.value) ||
    !isNullableNumber(overview.turnover.previous) ||
    !isNonEmptyString(overview.turnover.currency) ||
    !isRecord(overview.averageCheck) ||
    !isNullableNumber(overview.averageCheck.value) ||
    !isNullableNumber(overview.averageCheck.previous) ||
    !isRecord(overview.newAccounts) ||
    !isNonNegativeInteger(overview.newAccounts.value) ||
    !isNullableNumber(overview.newAccounts.previous) ||
    !isRecord(overview.noShowRate) ||
    !isNullablePercentage(overview.noShowRate.value) ||
    !isNullablePercentage(overview.noShowRate.previous) ||
    !isNonNegativeInteger(overview.noShowRate.count) ||
    !isNonNegativeInteger(overview.noShowRate.sample) ||
    !isRecord(overview.stuckBookings) ||
    !isNonNegativeInteger(overview.stuckBookings.value)
  ) {
    return false
  }

  if (
    (series.granularity !== 'day' && series.granularity !== 'week') ||
    !Array.isArray(series.points) ||
    !series.points.every(
      (point) =>
        isRecord(point) &&
        isStatisticsDay(point.date) &&
        isNonNegativeInteger(point.created) &&
        isNonNegativeInteger(point.completed) &&
        isNonNegativeInteger(point.cancelled) &&
        isNonNegativeInteger(point.noShow)
    )
  ) {
    return false
  }

  if (series.granularity === 'day' && series.points.length !== period.days) {
    return false
  }

  if (
    !isNonNegativeInteger(statuses.pending) ||
    !isNonNegativeInteger(statuses.confirmed) ||
    !isNonNegativeInteger(statuses.completed) ||
    !isNonNegativeInteger(statuses.cancelled) ||
    !isNonNegativeInteger(statuses.noShow) ||
    !isNonNegativeInteger(statuses.due) ||
    !isNonNegativeInteger(statuses.closed)
  ) {
    return false
  }

  if (
    !isNonNegativeInteger(clients.withVisit) ||
    !isNonNegativeInteger(clients.returning) ||
    !isNonNegativeInteger(clients.firstVisit) ||
    !isNonNegativeInteger(clients.guestBookings) ||
    !isNullablePercentage(clients.guestShare) ||
    !isNullablePercentage(clients.reviewRate)
  ) {
    return false
  }

  if (
    !isNonNegativeInteger(supply.activeBarbers) ||
    !isNonNegativeInteger(supply.workingBarbers) ||
    !isNonNegativeInteger(supply.idleBarbers) ||
    !isNonNegativeInteger(supply.newProfiles) ||
    !Array.isArray(supply.salonsByKind) ||
    !supply.salonsByKind.every(
      (kind) =>
        isRecord(kind) &&
        isNonEmptyString(kind.kind) &&
        isNonNegativeInteger(kind.listed) &&
        isNonNegativeInteger(kind.unlisted)
    )
  ) {
    return false
  }

  if (
    !isRecord(top.barbers) ||
    !Array.isArray(top.barbers.rows) ||
    !top.barbers.rows.every(isBarberTopRow) ||
    !isTopOther(top.barbers.other) ||
    !isRecord(top.services) ||
    !Array.isArray(top.services.rows) ||
    !top.services.rows.every(isServiceTopRow) ||
    !isTopOther(top.services.other) ||
    !isRecord(top.cities) ||
    !Array.isArray(top.cities.rows) ||
    !top.cities.rows.every(isCityTopRow) ||
    !isTopOther(top.cities.other)
  ) {
    return false
  }

  if (
    !isNullableNumber(quality.rating) ||
    !isNonNegativeInteger(quality.reviews) ||
    !Array.isArray(quality.distribution) ||
    quality.distribution.length !== 5 ||
    !quality.distribution.every(isNonNegativeInteger) ||
    !isNullablePercentage(quality.publishedShare) ||
    !isModeration(quality.requests) ||
    !isModeration(quality.salons)
  ) {
    return false
  }

  return true
}

/**
 * Поля заявки «Стать барбером» без проверки контейнера.
 *
 * Общие для своего и админского контракта: админская строка — та же заявка плюс
 * автор решения и заявитель, поэтому поля не дублируются, а дополняются.
 */
function hasBarberRequestFields(value: Record<string, unknown>): boolean {  return (
    isNonEmptyString(value.id) &&
    isBarberRequestStatus(value.status) &&
    isString(value.displayName) &&
    isNumber(value.yearsOfExperience) &&
    isString(value.city) &&
    isNullableString(value.about) &&
    isBoolean(value.hasSalon) &&
    isString(value.createdAt) &&
    isNullableString(value.reviewedAt) &&
    isNullableString(value.rejectionReason)
  )
}

/**
 * Заявка «Стать барбером» в своём контракте (`BarberRequestResponse`).
 *
 * `about` и `rejectionReason` контракт допускает `null`; `status` — одно из трёх
 * известных значений, поэтому новый статус сервера станет ошибкой экрана, а не
 * сломанной строкой. Даты остаются строками: формат разбирает `Intl` на краю.
 */
export function isBarberRequest(value: unknown): value is BarberRequest {
  return isRecord(value) && hasBarberRequestFields(value)
}

/** `GET api/barber-requests/mine`: заявка или `null`, когда её ещё нет. */
export function isMyBarberRequest(
  value: unknown
): value is MyBarberRequestResponse {
  return (
    isRecord(value) &&
    (value.request === null || isBarberRequest(value.request))
  )
}

/** Заявитель в админской строке: имя и email, id — как у любого DTO. */
function isAdminBarberRequestApplicant(
  value: unknown
): value is AdminBarberRequestApplicant {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isString(value.fullName) &&
    isString(value.email)
  )
}

/**
 * Админская строка заявки: те же поля плюс автор решения и заявитель.
 *
 * `reviewedByUserId` — `null` до решения; `applicant` обязателен, потому что
 * очередь показывает, кто подал заявку, а не только её содержимое.
 */
export function isAdminBarberRequest(
  value: unknown
): value is AdminBarberRequest {
  return (
    isRecord(value) &&
    hasBarberRequestFields(value) &&
    isNullableString(value.reviewedByUserId) &&
    isAdminBarberRequestApplicant(value.applicant)
  )
}

export const isAdminBarberRequestPage = isPageOf(isAdminBarberRequest)

/**
 * Строка инбокса напоминаний.
 *
 * `kind` и `delivery` — только известные имена: новый серверный статус должен стать
 * ошибкой экрана, а не сломанной строкой (§9.1). Инварианты домена проверяются тем же
 * guard'ом: у `Upcoming` обязательны `bookingId` и `startAt`, у `Digest` —
 * `bookingsCount` и `startAt` (начало первой записи дня), `bookingId` при этом `null`.
 * `readAt` — `null` или строка; формат дат не разбирается.
 */
export function isBarberNotification(
  value: unknown
): value is BarberNotification {
  if (
    !isRecord(value) ||
    !isBarberNotificationKind(value.kind) ||
    !isBarberNotificationDelivery(value.delivery) ||
    !isString(value.createdAt) ||
    !isNullableString(value.readAt) ||
    !isNullableString(value.serviceName) ||
    !isNullableString(value.salonName) ||
    !isNullableString(value.salonTimeZoneId) ||
    !isNullable(isNonEmptyString)(value.bookingId)
  ) {
    return false
  }

  if (value.kind === 'Upcoming') {
    return (
      isNonEmptyString(value.bookingId) &&
      isNonEmptyString(value.startAt) &&
      value.bookingsCount === null
    )
  }

  return (
    value.bookingId === null &&
    isNonEmptyString(value.startAt) &&
    isNonNegativeInteger(value.bookingsCount)
  )
}

export const isBarberNotificationPage = isPageOf(isBarberNotification)

/** `GET api/barber/notifications/unread-count`: целое не меньше нуля. */
export function isUnreadNotificationsPayload(
  value: unknown
): value is { count: number } {
  return isRecord(value) && isNonNegativeInteger(value.count)
}

/**
 * `NotificationSettingsResponse`.
 *
 * `digestTime` — готовая строка `HH:mm`, `leadMinutes` — одно из трёх значений домена.
 * `telegramLinkedAt` может быть `null`. Поля `chatId` в контракте нет: клиент получает
 * только `telegramLinked` и `botConfigured`, а лишние поля ответа guard игнорирует
 * по общему правилу (§9.1).
 */
export function isNotificationSettings(
  value: unknown
): value is NotificationSettings {
  return (
    isRecord(value) &&
    isBoolean(value.isEnabled) &&
    isBoolean(value.digestEnabled) &&
    isDigestTime(value.digestTime) &&
    isNotificationLeadMinutes(value.leadMinutes) &&
    isBoolean(value.telegramLinked) &&
    isNullableString(value.telegramLinkedAt) &&
    isBoolean(value.botConfigured)
  )
}

/** Ответ `POST api/barber/notifications/telegram/link`. */
export function isTelegramLink(value: unknown): value is TelegramLink {
  return (
    isRecord(value) &&
    isNonEmptyString(value.code) &&
    isNonEmptyString(value.deepLink) &&
    isString(value.expiresAt)
  )
}




