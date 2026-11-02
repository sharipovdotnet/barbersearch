import { ApiError, apiRequest } from '@/lib/api'
import type { AdminUserRole } from '@/lib/admin-users'
import { ADMIN_USER_ROLE_LABELS } from '@/lib/admin-users'
import { BARBER_GRADE_LABELS, type BarberGrade } from '@/lib/barber-grade'
import { describeApiFailure } from '@/lib/errors'
import { isAdminAuditPage } from '@/lib/response-guards'

/**
 * Админское чтение журнала изменений (`GET api/admin/audit`).
 *
 * Журнал неизменяем: здесь есть только чтение, и никаких `POST`/`PUT`/`DELETE`
 * для него не будет. Строка несёт `entity_type` + `entity_id`, а не имя сущности:
 * имя могло быть переименовано или удалено, и «актуальное» имя рядом с историческим
 * diff'ом только вводило бы в заблуждение.
 *
 * Аудит покрывает `User`, `BarberProfile` и `Review` (allowlist `AuditTrail`).
 * Правки каталога, салонов, услуг, специализаций и броней в журнал не попадают —
 * это известное ограничение, и экран его не обещает.
 */

/** Тип сущности фильтра: `all` — без фильтра, остальные три — allowlist аудита. */
export type AdminAuditEntityType = 'all' | 'user' | 'barber_profile' | 'review'

/** Порядок опций фильтра: от широкого к узкому. */
export const ADMIN_AUDIT_ENTITY_TYPES: readonly AdminAuditEntityType[] = [
  'all',
  'user',
  'barber_profile',
  'review',
]

export const ADMIN_AUDIT_ENTITY_LABELS: Record<AdminAuditEntityType, string> = {
  all: 'Все сущности',
  user: 'Пользователь',
  barber_profile: 'Барбер',
  review: 'Отзыв',
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
 */
export const ADMIN_AUDIT_FIELD_LABELS: Record<string, string> = {
  email: 'Email',
  role: 'Роль',
  isActive: 'Доступ',
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

  return ADMIN_AUDIT_ACTION_LABELS[tail] ?? tail
}

/**
 * Значение поля diff'а для показа.
 *
 * `null` — прочерк (для создания и удаления это честно), boolean — «Да»/«Нет»,
 * `[redacted]` — «скрыто». Роль и грейд получают русские подписи, но неизвестное
 * значение остаётся исходной строкой. Никакой HTML не собирается.
 */
export function formatAuditValue(
  field: string,
  value: AdminAuditScalar | null
): string {
  if (value === null) {
    return '—'
  }

  if (typeof value === 'boolean') {
    return value ? 'Да' : 'Нет'
  }

  if (value === '[redacted]') {
    return 'скрыто'
  }

  if (field === 'role') {
    return ADMIN_USER_ROLE_LABELS[value as AdminUserRole] ?? String(value)
  }

  if (field === 'grade') {
    return BARBER_GRADE_LABELS[value as BarberGrade] ?? String(value)
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
    return fallback
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  if (cause.hasCode('admin.audit.cursor.invalid')) {
    return 'Список устарел. Обновите и начните с первой страницы.'
  }

  if (cause.status === 429) {
    return 'Слишком много запросов. Подождите минуту и повторите.'
  }

  return describeApiFailure(cause, fallback)
}
