import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import type { AdminUserRole } from '@/lib/admin-users'
import { ADMIN_USER_ROLE_LABELS } from '@/lib/admin-users'
import { BARBER_GRADE_LABELS, type BarberGrade } from '@/lib/barber-grade'
import {
  SERVICE_CATEGORY_LABELS,
  type ServiceCategory,
} from '@/lib/barber-profile-labels'
import { bookingStatusLabel } from '@/lib/booking-status'
import type { BookingStatus } from '@/lib/bookings'
import { describeApiFailure } from '@/lib/errors'
import {
  photoModerationLabel,
  type PhotoModerationStatus,
} from '@/lib/photo-moderation'
import { isAdminAuditPage } from '@/lib/response-guards'
import {
  SALON_MODERATION_LABELS,
  type SalonModerationStatus,
} from '@/lib/salon-moderation'

/**
 * Админское чтение журнала изменений (`GET api/admin/audit`).
 *
 * Журнал неизменяем: здесь есть только чтение, и никаких `POST`/`PUT`/`DELETE`
 * для него не будет. Строка несёт `entity_type` + `entity_id`, а не имя сущности:
 * имя могло быть переименовано или удалено, и «актуальное» имя рядом с историческим
 * diff'ом только вводило бы в заблуждение.
 *
 * Аудит покрывает восемь типов (`allowlist` `AuditTrail`): `User`, `BarberProfile`,
 * `Review`, `Salon`, `HairService`, `Specialization`, `Booking` и `PortfolioPhoto`;
 * у брони пишутся только смена статуса и заметка барбера. Создание брони сознательно
 * не аудируется — бронь уже хранит владельца, время и цену, а строка на каждую
 * бронь раздувала бы журнал без новой информации. `PortfolioPhoto` — исключение
 * среди детей `BarberProfile`: у кадра отдельный тип, потому что загрузка и
 * удаление — это жизненный цикл контента, а `BarberService`, `BarberSalon`,
 * `WorkingSchedule` и `TimeOff` в allowlist не входят. Ретенции,
 * партиционирования и экспорта нет, поэтому журнал растёт монотонно и читается
 * страницами. Удалений каталога в API нет, и экран не обещает «настоящую»
 * историю удалений.
 */

/** Тип сущности фильтра: `all` — без фильтра, остальные восемь — allowlist аудита. */
export type AdminAuditEntityType =
  | 'all'
  | 'user'
  | 'barber_profile'
  | 'review'
  | 'salon'
  | 'hair_service'
  | 'specialization'
  | 'booking'
  | 'portfolio_photo'

/** Порядок опций фильтра: прежние значения не сдвигаются, новые дописаны. */
export const ADMIN_AUDIT_ENTITY_TYPES: readonly AdminAuditEntityType[] = [
  'all',
  'user',
  'barber_profile',
  'review',
  'salon',
  'hair_service',
  'specialization',
  'booking',
  'portfolio_photo',
]

export const ADMIN_AUDIT_ENTITY_LABELS: Record<AdminAuditEntityType, string> = {
  all: 'Все сущности',
  user: 'Пользователь',
  barber_profile: 'Барбер',
  review: 'Отзыв',
  salon: 'Салон',
  hair_service: 'Услуга',
  specialization: 'Специализация',
  booking: 'Бронь',
  portfolio_photo: 'Фото портфолио',
}

/**
 * Подписи действий: `action` приходит как `{entity}.{created|updated|deleted}`,
 * поэтому подпись ищется по хвосту после последней точки, а не по всей строке.
 */
export const ADMIN_AUDIT_ACTION_LABELS: Record<string, string> = {
  created: 'Создано',
  updated: 'Изменено',
  deleted: 'Удалено',
}

/**
 * Русские подписи полей allowlist. Неизвестное поле показывается camelCase как есть:
 * выдуманное название скрыло бы, что сервер прислал новое поле.
 *
 * Здесь только поля, которые реально приходят с расширенным allowlist. Подписи
 * `startAt`/`endAt`/`price`/`salonId`/`barberServiceId` появятся вместе с переносом
 * и правкой цены, а не заранее.
 */
export const ADMIN_AUDIT_FIELD_LABELS: Record<string, string> = {
  email: 'Email',
  role: 'Роль',
  // «Активность» вместо «Доступ»: подпись обслуживает и пользователя, и строку
  // каталога, где «Доступ: Да» читалось бы неверно.
  isActive: 'Активность',
  // Флаг модерации аватара: «Фото скрыто администратором: Нет → Да» в журнале.
  isAvatarBlocked: 'Фото скрыто администратором',
  isEmailVerified: 'Email подтверждён',
  isPhoneVerified: 'Телефон подтверждён',
  passwordHash: 'Пароль',
  securityStamp: 'Метка безопасности',
  barberProfileId: 'Профиль барбера',
  grade: 'Грейд',
  rating: 'Рейтинг',
  reviewsCount: 'Отзывов',
  userId: 'Пользователь',
  bookingId: 'Бронь',
  text: 'Текст отзыва',
  isPublished: 'Опубликован',
  // Поля кадра: URL показывается как есть — по нему администратор находит файл.
  // `thumbnailUrl`, причина отказа и reviewed-поля в payload не входят.
  url: 'Файл',
  sortOrder: 'Порядок',
  isBlocked: 'Скрыто администратором',
  moderationStatus: 'Статус проверки',
  avatarUrl: 'Фото профиля',
  avatarModerationStatus: 'Статус проверки фото',
  name: 'Название',
  description: 'Описание',
  category: 'Категория',
  defaultDurationMinutes: 'Длительность, мин',
  address: 'Адрес',
  city: 'Город',
  postalCode: 'Индекс',
  country: 'Страна',
  timeZoneId: 'Пояс салона',
  latitude: 'Широта',
  longitude: 'Долгота',
  phoneNumber: 'Телефон',
  websiteUrl: 'Сайт',
  status: 'Статус',
  barberNote: 'Заметка барбера',
  cancellationReason: 'Причина отмены',
}

/**
 * Значение diff'а — JSON-скаляр как есть.
 *
 * Секреты сервер уже заменил на `[redacted]`, длинный текст обрезан маркером `...`;
 * клиент ничего не раскрывает и не вставляет HTML.
 */
export type AdminAuditScalar = string | number | boolean

export type AdminAuditActor = {
  id: string
  /** `null` у удалённого автора: строка остаётся, email больше не резолвится. */
  email: string | null
}

export type AdminAuditChange = {
  field: string
  /** Для создания и удаления `from = null`, `to` — записанное значение. */
  from: AdminAuditScalar | null
  to: AdminAuditScalar | null
}

export type AdminAuditEntry = {
  id: string
  occurredAt: string
  action: string
  entityType: string
  entityId: string
  /** `null` — системная запись: действие без автора (`actor_user_id = null`). */
  actor: AdminAuditActor | null
  changes: AdminAuditChange[]
}

export type AdminAuditPage = {
  items: AdminAuditEntry[]
  nextCursor: string | null
}

/**
 * Размер страницы совпадает с дефолтом сервера.
 *
 * В запрос `pageSize` не добавляется: контракт чтения — `entityType` всегда,
 * `actor`/`from`/`to` только непустыми, `cursor` только при наличии (§4.6).
 */
export const ADMIN_AUDIT_PAGE_SIZE = 20

/** Максимальная длина фильтра автора на сервере; длиннее он ответит 400. */
export const ADMIN_AUDIT_ACTOR_MAX_LENGTH = 100

/** Подпись действия по хвосту `action`: `user.updated` → «Изменено». */
export function formatAuditAction(action: string): string {
  const tail = action.slice(action.lastIndexOf('.') + 1)

  return t(ADMIN_AUDIT_ACTION_LABELS[tail] ?? tail)
}

/**
 * Значение поля diff'а для показа.
 *
 * `null` — прочерк (для создания и удаления это честно), boolean — «Да»/«Нет»,
 * `[redacted]` — «скрыто». Роль, грейд, статус брони, категория услуги и статусы
 * проверки получают русские подписи. Для `moderationStatus` подпись зависит от
 * типа сущности: `salon` — `SALON_MODERATION_LABELS`, `portfolio_photo` —
 * `PHOTO_MODERATION_LABELS`, остальные типы — прежнее поведение. Неизвестное
 * значение остаётся исходной строкой сервера. Никакой HTML не собирается.
 */
export function formatAuditValue(
  field: string,
  value: AdminAuditScalar | null,
  entityType?: string
): string {
  if (value === null) {
    return '—'
  }

  if (typeof value === 'boolean') {
    return value ? t('Да') : t('Нет')
  }

  if (value === '[redacted]') {
    return t('скрыто')
  }

  if (field === 'role') {
    return t(ADMIN_USER_ROLE_LABELS[value as AdminUserRole] ?? String(value))
  }

  if (field === 'grade') {
    return t(BARBER_GRADE_LABELS[value as BarberGrade] ?? String(value))
  }

  // Статус проверки фото общий у портфолио и аватара: подпись не зависит от типа.
  if (field === 'avatarModerationStatus' && typeof value === 'string') {
    return photoModerationLabel(value as PhotoModerationStatus)
  }

  if (field === 'moderationStatus' && typeof value === 'string') {
    if (entityType === 'salon') {
      return t(SALON_MODERATION_LABELS[value as SalonModerationStatus] ?? value)
    }

    if (entityType === 'portfolio_photo') {
      return photoModerationLabel(value as PhotoModerationStatus)
    }

    // Прочие типы сущностей показывают серверную строку, как раньше.
    return value
  }

  // Проверяемый каст: обе функции сами возвращают исходную строку для
  // неизвестного значения, поэтому приведение типа здесь безопасно.
  if (field === 'status' && typeof value === 'string') {
    return t(bookingStatusLabel(value as BookingStatus))
  }

  if (field === 'category' && typeof value === 'string') {
    return t(SERVICE_CATEGORY_LABELS[value as ServiceCategory] ?? value)
  }

  return String(value)
}

/**
 * Страница журнала: свежие сверху, порядок и `nextCursor` задаёт сервер.
 *
 * `entityType` уходит всегда (`all` — без фильтра), `actor`/`from`/`to` — только
 * непустыми, `cursor` — только при наличии. Фильтры живут в состоянии React и в URL
 * не пишутся: email автора — PII, ему не место в истории браузера и логах прокси.
 */
export async function fetchAdminAuditEntries({
  entityType,
  actor,
  from,
  to,
  cursor,
  signal,
}: {
  entityType: AdminAuditEntityType
  actor?: string
  from?: string
  to?: string
  cursor?: string | null
  signal?: AbortSignal
}): Promise<AdminAuditPage> {
  const query = new URLSearchParams({ entityType })

  if (actor) {
    query.set('actor', actor)
  }

  if (from) {
    query.set('from', from)
  }

  if (to) {
    query.set('to', to)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminAuditPage>(
    `/admin/audit?${query.toString()}`,
    { signal },
    { validate: isAdminAuditPage }
  )
}

/**
 * Текст ошибки журнала: публичные коды важнее статуса, английский `detail` не выходит.
 *
 * `403` на чтении — это роль, а не «нет данных»; устаревший курсор предлагает начать
 * с первой страницы, потому что страницы кэшированы в памяти и продолжать нечего.
 */
export function describeAdminAuditError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return t(fallback)
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return t('Раздел доступен только администратору.')
  }

  if (cause.hasCode('admin.audit.cursor.invalid')) {
    return t('Список устарел. Обновите и начните с первой страницы.')
  }

  if (cause.status === 429) {
    return t('Слишком много запросов. Подождите минуту и повторите.')
  }

  return describeApiFailure(cause, fallback)
}
