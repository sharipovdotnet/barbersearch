import type {
  AdminBarber,
  AdminBarberDetail,
  AdminBarberReadiness,
} from '@/lib/admin-barbers'
import type { AdminReview } from '@/lib/admin-reviews'
import type {
  AdminUserBarberProfile,
  AdminUserDetail,
  AdminUserRole,
  AdminUserRow,
} from '@/lib/admin-users'
import type { SessionUser } from '@/lib/auth'
import type { BarberBooking } from '@/lib/barber-bookings'
import type { BarberGrade } from '@/lib/barber-grade'
import type { DayOfWeek, Language, PaymentMethod, ServiceCategory } from '@/lib/barber-profile-labels'
import type { BarberSearchItem } from '@/lib/barber-search'
import type { BarberSlot, BarberSlots } from '@/lib/barber-slots'
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
import type { Page } from '@/lib/paging'
import type { Review } from '@/lib/reviews'
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

const isBookingStatus = isOneOf(BOOKING_STATUSES)
const isBarberGrade = isOneOf(BARBER_GRADES)
const isLanguage = isOneOf(LANGUAGES)
const isPaymentMethod = isOneOf(PAYMENT_METHODS)
const isServiceCategory = isOneOf(SERVICE_CATEGORIES)
const isDayOfWeek = isOneOf(DAYS_OF_WEEK)
const isUserRole = isOneOf(USER_ROLES)

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

export function isBooking(value: unknown): value is Booking {
  return (
    isRecord(value) &&
    isNonEmptyString(value.id) &&
    isNonEmptyString(value.userId) &&
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
    isNullableString(value.city)
  )
}

export const isBarberSearchPage = isPageOf(isBarberSearchItem)

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
    isString(value.latePolicy) &&
    isBoolean(value.isActive) &&
    isBoolean(value.isVerified) &&
    isArrayOf(isProfileService)(value.services) &&
    isArrayOf(isProfileSalon)(value.salons) &&
    isArrayOf(isSpecialization)(value.specializations)
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
    isString(value.createdAt) &&
    isString(value.updatedAt)
  )
}

export const isAdminUserPage = isPageOf(isAdminUserRow)
