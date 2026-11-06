import type { CatalogueTab } from '@/lib/admin-path'
import { ApiError, apiRequest } from '@/lib/api'
import type { ServiceCategory } from '@/lib/barber-profile-labels'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { PlaceKind } from '@/lib/place-kind'
import type {
  SalonContactInput,
  SalonInput,
  SalonLocationInput,
} from '@/lib/salon-form'
import {
  SALON_REJECTION_MAX_LENGTH,
  type SalonModerationStatus,
} from '@/lib/salon-moderation'
import {
  isAdminHairServiceList,
  isAdminSalonList,
  isAdminSpecializationList,
  isCreatedId,
} from '@/lib/response-guards'

/**
 * Админский каталог: три справочника, мутации существующих эндпоинтов и модерация
 * салонов.
 *
 * Чтения новые (`GET api/admin/catalogue/*`): они отдают полный справочник, включая
 * выключенные строки и очередь проверки салонов, поэтому администратор может найти
 * строку и вернуть её обратно. Мутации остаются на публичных адресах
 * `api/hair-services`, `api/specializations` и `api/salons`; решения по салону —
 * отдельные админские `approval`/`rejection` на том же контроллере.
 *
 * Пагинации здесь нет намеренно: справочники маленькие, а созданная строка должна быть
 * видна сразу. Это исключение из правила «список = пейджер».
 */

// Вкладка принадлежит маршруту: `?tab=` разбирает `admin-path.ts`. Реэкспорт нужен,
// чтобы экран и хук брали типы справочников из одного модуля.
export type { CatalogueTab } from '@/lib/admin-path'

export type AdminCatalogueStatus = 'all' | 'active' | 'hidden' | 'pending'

/**
 * Статусы общего фильтра. `pending` есть только у салонов: у услуг и специализаций
 * модерации нет, и сервер отвечает на него `400 admin.catalogue.status`.
 */
export const ADMIN_CATALOGUE_STATUSES: readonly AdminCatalogueStatus[] = [
  'all',
  'active',
  'hidden',
]

/** Четвёртый фильтр вкладки «Салоны» — очередь проверки. */
export const ADMIN_SALON_CATALOGUE_STATUSES: readonly AdminCatalogueStatus[] = [
  ...ADMIN_CATALOGUE_STATUSES,
  'pending',
]

/** Набор фильтров вкладки: `pending` не должен появляться у услуг и специализаций. */
export function catalogueStatuses(
  tab: CatalogueTab
): readonly AdminCatalogueStatus[] {
  return tab === 'salons'
    ? ADMIN_SALON_CATALOGUE_STATUSES
    : ADMIN_CATALOGUE_STATUSES
}

export const ADMIN_CATALOGUE_STATUS_LABELS: Record<AdminCatalogueStatus, string> = {
  all: 'Все',
  active: 'Активные',
  hidden: 'Выключенные',
  pending: 'На проверке',
}

/**
 * Название строки в винительном падеже: кнопка «Создать услугу» и вопрос
 * «Выключить услугу?» подставляют его внутрь фразы, а не в начало.
 */
export const ADMIN_CATALOGUE_ENTITY_LABELS: Record<CatalogueTab, string> = {
  services: 'Услугу',
  specializations: 'Специализацию',
  salons: 'Салон',
}

/** Подпись вкладки: без цифр, API не отдаёт общее число строк. */
export const ADMIN_CATALOGUE_TAB_LABELS: Record<CatalogueTab, string> = {
  services: 'Услуги',
  specializations: 'Специализации',
  salons: 'Салоны',
}

/** `caption` таблицы: скринридеру нужен заголовок справочника. */
export const ADMIN_CATALOGUE_CAPTIONS: Record<CatalogueTab, string> = {
  services: 'Справочник услуг',
  specializations: 'Справочник специализаций',
  salons: 'Справочник салонов',
}

export const ADMIN_CATALOGUE_EMPTY: Record<CatalogueTab, string> = {
  services: 'В справочнике услуг пока нет строк.',
  specializations: 'В справочнике специализаций пока нет строк.',
  salons: 'В справочнике салонов пока нет строк.',
}

export const ADMIN_CATALOGUE_SEARCH_LABELS: Record<CatalogueTab, string> = {
  services: 'Поиск по названию',
  specializations: 'Поиск по названию',
  salons: 'Поиск по названию и городу',
}

/** Статус строки — текстом, не только цветом. */
export const ADMIN_CATALOGUE_ACTIVITY_LABELS: Record<
  CatalogueTab,
  { active: string; hidden: string }
> = {
  services: { active: 'Активна', hidden: 'Выключена' },
  specializations: { active: 'Активна', hidden: 'Выключена' },
  salons: { active: 'Активен', hidden: 'Выключен' },
}

/** Максимальная длина поисковой строки на сервере; длиннее он ответит 400. */
export const ADMIN_CATALOGUE_SEARCH_MAX_LENGTH = 100

export type AdminHairService = {
  id: string
  name: string
  category: ServiceCategory
  defaultDurationMinutes: number
  description: string | null
  isActive: boolean
  usageCount: number
}

export type AdminSpecialization = {
  id: string
  name: string
  description: string | null
  isActive: boolean
  usageCount: number
}

export type AdminSalon = {
  id: string
  name: string
  address: string
  city: string
  postalCode: string | null
  country: string
  timeZoneId: string
  latitude: number
  longitude: number
  phoneNumber: string | null
  websiteUrl: string | null
  isActive: boolean
  moderationStatus: SalonModerationStatus
  /** Тип места: строка каталога показывает его рядом с названием. */
  kind: PlaceKind
  /** `false` — место скрыто из каталога и не проходило модерацию. */
  isListed: boolean
  /** Причина отказа; `null` у салона не в статусе `Rejected`. */
  rejectionReason: string | null
  /** Когда администратор принял решение; `null` у салона, не проходившего проверку. */
  reviewedAt: string | null
  /** Email владельца-барбера; `null` у салона, созданного администратором. */
  ownerEmail: string | null
  usageCount: number
}

export type AdminCatalogueRow = AdminHairService | AdminSpecialization | AdminSalon

type ListOptions = {
  status: AdminCatalogueStatus
  search?: string
  signal?: AbortSignal
}

/**
 * Общий запрос списка: `status` уходит всегда, `search` — только непустым.
 *
 * Пустой поиск не отправляется: сервер трактует его как «без фильтра», но лишний параметр
 * в URL делает ссылки и логи шумными.
 */
async function fetchCatalogueList<T>(
  path: string,
  { status, search, signal }: ListOptions,
  validate: (body: unknown) => body is T[]
): Promise<T[]> {
  const query = new URLSearchParams({ status })
  const trimmed = search?.trim()

  if (trimmed) {
    query.set('search', trimmed)
  }

  return apiRequest<T[]>(`${path}?${query.toString()}`, { signal }, { validate })
}

export async function fetchAdminHairServices(
  options: ListOptions
): Promise<AdminHairService[]> {
  return fetchCatalogueList(
    '/admin/catalogue/hair-services',
    options,
    isAdminHairServiceList
  )
}

export async function fetchAdminSpecializations(
  options: ListOptions
): Promise<AdminSpecialization[]> {
  return fetchCatalogueList(
    '/admin/catalogue/specializations',
    options,
    isAdminSpecializationList
  )
}

export async function fetchAdminSalons(options: ListOptions): Promise<AdminSalon[]> {
  return fetchCatalogueList('/admin/catalogue/salons', options, isAdminSalonList)
}

export type HairServiceInput = {
  name: string
  category: ServiceCategory
  defaultDurationMinutes: number
  description: string | null
}

export type HairServiceUpdateInput = {
  name: string
  description: string | null
  defaultDurationMinutes: number
}

export async function createHairService(
  input: HairServiceInput
): Promise<{ id: string }> {
  return mutateWithCsrf<{ id: string }>('/hair-services', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })
}

export async function updateHairService(
  hairServiceId: string,
  input: HairServiceUpdateInput
): Promise<void> {
  await mutateWithCsrf<void>(`/hair-services/${hairServiceId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export async function setHairServiceActive(
  hairServiceId: string,
  isActive: boolean
): Promise<void> {
  await mutateWithCsrf<void>(`/hair-services/${hairServiceId}/activation`, {
    method: isActive ? 'POST' : 'DELETE',
  })
}

export type SpecializationInput = {
  name: string
  description: string | null
}

export async function createSpecialization(
  input: SpecializationInput
): Promise<{ id: string }> {
  return mutateWithCsrf<{ id: string }>('/specializations', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })
}

export async function renameSpecialization(
  specializationId: string,
  input: SpecializationInput
): Promise<void> {
  await mutateWithCsrf<void>(`/specializations/${specializationId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export async function setSpecializationActive(
  specializationId: string,
  isActive: boolean
): Promise<void> {
  await mutateWithCsrf<void>(`/specializations/${specializationId}/activation`, {
    method: isActive ? 'POST' : 'DELETE',
  })
}

export async function createSalon(input: SalonInput): Promise<{ id: string }> {
  return mutateWithCsrf<{ id: string }>('/salons', {
    method: 'POST',
    body: JSON.stringify(input),
    validate: isCreatedId,
  })
}

export async function updateSalonLocation(
  salonId: string,
  input: SalonLocationInput
): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/location`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export async function updateSalonContactInfo(
  salonId: string,
  input: SalonContactInput
): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/contact-info`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export async function setSalonActive(
  salonId: string,
  isActive: boolean
): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/activation`, {
    method: isActive ? 'POST' : 'DELETE',
  })
}

export type SalonModerationAction = 'approve' | 'reject'

/** Одобрение салона: `POST /api/salons/{id}/approval`, тела нет. */
export async function approveSalon(salonId: string): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/approval`, {
    method: 'POST',
  })
}

/** Отказ: причина обязательна, её увидит владелец в «Моих салонах». */
export async function rejectSalon(
  salonId: string,
  reason: string
): Promise<void> {
  await mutateWithCsrf<void>(`/salons/${salonId}/rejection`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  })
}

/**
 * Текст ошибки каталога: публичные коды важнее статуса, английский `detail` не показывается.
 *
 * `fallback` приходит с экрана: у списка и у каждой мутации он свой.
 */
export function describeAdminCatalogueError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return fallback
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  if (cause.status === 404) {
    return 'Строка не найдена — обновите список.'
  }

  if (
    cause.status === 409
    && cause.codes.some((code) => code.endsWith('update.concurrencyConflict'))
  ) {
    return 'Запись изменили одновременно. Обновите список.'
  }

  if (
    cause.hasCode('specialization.create.nameTaken')
    || cause.hasCode('specialization.rename.nameTaken')
    || cause.hasCode('specialization.add.uniqueViolation')
  ) {
    return 'Специализация с таким названием уже есть.'
  }

  if (cause.hasCode('salon.timeZoneId.unknown')) {
    return 'Неизвестный часовой пояс. Выберите зону из списка.'
  }

  if (cause.hasCode('salon.city') || cause.hasCode('salon.city.invalid')) {
    return 'Город не может быть пустым.'
  }

  if (cause.status === 429) {
    return 'Слишком много действий подряд. Подождите минуту.'
  }

  return describeApiFailure(cause, fallback)
}

/**
 * Ошибки одобрения и отказа салона.
 *
 * `409 salon.review.invalidStatus` значит, что салон уже рассмотрели в другой вкладке:
 * помочь может только повторное чтение. `400 salon.reject.*` показываются у поля причины.
 */
export function describeSalonModerationError(
  cause: unknown,
  action: SalonModerationAction
): string {
  if (cause instanceof ApiError) {
    if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
      return 'Раздел доступен только администратору.'
    }

    if (cause.status === 404 || cause.hasCode('salon.notFound')) {
      return 'Салон больше не существует. Обновите список.'
    }

    if (cause.hasCode('salon.review.invalidStatus') || cause.status === 409) {
      return 'Салон уже проверили. Обновите список.'
    }

    if (cause.hasCode('salon.reject.reasonRequired')) {
      return 'Укажите причину отказа.'
    }

    if (cause.hasCode('salon.reject.reasonTooLong')) {
      return `Слишком длинная причина — максимум ${SALON_REJECTION_MAX_LENGTH} символов.`
    }

    if (cause.hasCode('auth.csrf.invalid')) {
      return 'Сессия устарела. Обновите страницу и повторите действие.'
    }
  }

  return describeApiFailure(
    cause,
    action === 'approve'
      ? 'Не удалось одобрить салон. Попробуйте ещё раз.'
      : 'Не удалось отклонить салон. Попробуйте ещё раз.'
  )
}
