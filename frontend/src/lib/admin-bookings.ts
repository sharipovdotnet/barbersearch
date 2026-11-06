import { ApiError, apiRequest } from '@/lib/api'
import type { BookingStatus } from '@/lib/bookings'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import { formatShortDateTime } from '@/lib/format'
import type { Page } from '@/lib/paging'
import { isAdminBooking, isAdminBookingPage } from '@/lib/response-guards'

/**
 * Брони всех клиентов глазами администратора.
 *
 * `GET api/admin/bookings` — единственное чтение проекта, которое отдаёт email
 * и телефон клиента: это PII, и она допустима только в админском контракте.
 * Поиск — email клиента или точный id брони, фильтр — статус; и то и другое
 * живёт в состоянии React и в URL не пишется.
 *
 * Новых мутаций здесь нет: отмена и неявка — существующие эндпоинты
 * `POST api/bookings/{id}/cancellation` и `.../no-show`. Брони не аудируются,
 * и экран этого не обещает.
 */

/** Статус брони: имена `BookingStatus` приходят строками, как везде в JSON. */
export type AdminBookingStatus = BookingStatus

/** Фильтр статуса: `all` — без фильтра и значение по умолчанию. */
export type AdminBookingStatusFilter = 'all' | AdminBookingStatus

export const ADMIN_BOOKING_STATUS_FILTERS: readonly AdminBookingStatusFilter[] = [
  'all',
  'Pending',
  'Confirmed',
  'Completed',
  'Cancelled',
  'NoShow',
]

/** Подписи фильтра целиком: строка списка берёт `bookingStatusLabel`. */
export const ADMIN_BOOKING_STATUS_FILTER_LABELS: Record<
  AdminBookingStatusFilter,
  string
> = {
  all: 'Все статусы',
  Pending: 'Ждёт подтверждения',
  Confirmed: 'Подтверждена',
  Completed: 'Завершена',
  Cancelled: 'Отменена',
  NoShow: 'Неявка',
}

export type AdminBookingCustomer = {
  /** `null` — человек записан без аккаунта. */
  id: string | null
  firstName: string
  lastName: string
  /** `null` у гостя: email есть только у зарегистрированного клиента. */
  email: string | null
  /** Телефон — PII: показывается только на карточке брони. */
  phoneNumber: string | null
}

export type AdminBookingBarber = {
  profileId: string
  displayName: string
}

export type AdminBookingService = {
  name: string
}

export type AdminBookingSalon = {
  id: string
  name: string
  address: string
  city: string
  /**
   * IANA-пояс салона: `startAt`/`endAt` приходят в UTC, а показывать их нужно
   * здесь — иначе «18:30» не совпадёт с тем, что клиент видел при записи.
   */
  timeZoneId: string
}

export type AdminBooking = {
  id: string
  status: AdminBookingStatus
  startAt: string
  endAt: string
  /** Снимок цены из брони, а не текущая цена услуги. */
  price: number
  customerNote: string | null
  /** Заметка барбера: админ её только читает. */
  barberNote: string | null
  /** Причина отмены, оставленная барбером: админ её только читает. */
  cancellationReason: string | null
  createdAt: string
  customer: AdminBookingCustomer
  barber: AdminBookingBarber
  service: AdminBookingService
  salon: AdminBookingSalon
}

export type AdminBookingsPage = Page<AdminBooking>

/**
 * Размер страницы совпадает с дефолтом сервера.
 *
 * `pageSize` в запрос не добавляется: контракт чтения — `status` всегда,
 * `search` только непустым, `cursor` только при наличии.
 */
export const ADMIN_BOOKINGS_PAGE_SIZE = 20

/** Максимальная длина поисковой строки на сервере; длиннее он ответит 400. */
export const ADMIN_BOOKINGS_SEARCH_MAX_LENGTH = 100

/** «Ирина Волкова», email или нейтральная подпись, если имени нет. */
export function getAdminBookingCustomerName(
  customer: Pick<AdminBookingCustomer, 'firstName' | 'lastName' | 'email'>
): string {
  const name = `${customer.firstName} ${customer.lastName}`.trim()
  return name === '' ? customer.email ?? 'Без имени' : name
}

/**
 * Контакт для списка: email зарегистрированного клиента, иначе телефон гостя.
 *
 * У гостя email нет, и пустая строка в таблице выглядела бы как сбой чтения.
 */
export function getAdminBookingCustomerContact(
  customer: Pick<AdminBookingCustomer, 'email' | 'phoneNumber'>
): string {
  return customer.email ?? customer.phoneNumber ?? 'Без аккаунта'
}

/**
 * Время визита в поясе салона.
 *
 * Пояс берётся из `salon.timeZoneId` самой брони: показывать UTC или пояс
 * браузера нельзя, поддержка и клиент должны видеть одно и то же время.
 */
export function formatAdminBookingTime(
  booking: Pick<AdminBooking, 'startAt' | 'salon'>
): string {
  return formatShortDateTime(booking.startAt, booking.salon.timeZoneId)
}

/**
 * Страница броней: свежие сверху, порядок и `nextCursor` задаёт сервер.
 *
 * `status` уходит всегда (`all` — без фильтра), пустой `search` не отправляется,
 * `cursor` — только при наличии. Фильтры остаются в состоянии React: email
 * клиента — PII, ему не место в истории браузера и логах прокси.
 */
export async function fetchAdminBookings({
  status = 'all',
  search,
  cursor,
  signal,
}: {
  status?: AdminBookingStatusFilter
  search?: string
  cursor?: string | null
  signal?: AbortSignal
} = {}): Promise<AdminBookingsPage> {
  const query = new URLSearchParams({ status })
  const trimmed = search?.trim()

  if (trimmed) {
    query.set('search', trimmed)
  }

  if (cursor) {
    query.set('cursor', cursor)
  }

  return apiRequest<AdminBookingsPage>(
    `/admin/bookings?${query.toString()}`,
    { signal },
    { validate: isAdminBookingPage }
  )
}

/** Карточка брони по прямому адресу; `404 admin.bookings.notFound` — не найдена. */
export async function fetchAdminBooking(
  bookingId: string,
  signal?: AbortSignal
): Promise<AdminBooking> {
  return apiRequest<AdminBooking>(
    `/admin/bookings/${encodeURIComponent(bookingId)}`,
    { signal },
    { validate: isAdminBooking }
  )
}

/**
 * Отмена чужой брони: `Pending|Confirmed → Cancelled`, `204`.
 *
 * Это существующая мутация владельца/админа, а не новый админский эндпоинт;
 * идёт через `mutateWithCsrf`, как остальные cookie-мутации.
 */
export async function cancelAdminBooking(bookingId: string): Promise<void> {
  await mutateWithCsrf<void>(
    `/bookings/${encodeURIComponent(bookingId)}/cancellation`,
    { method: 'POST' }
  )
}

/** Неявка: `Confirmed → NoShow`, только после начала визита; сервер проверит сам. */
export async function markAdminBookingNoShow(bookingId: string): Promise<void> {
  await mutateWithCsrf<void>(`/bookings/${encodeURIComponent(bookingId)}/no-show`, {
    method: 'POST',
  })
}

/**
 * Текст ошибки списка и карточки: публичные коды важнее статуса, `detail` не выходит.
 *
 * `403` на чтении — это роль, а не «нет данных»; устаревший курсор предлагает
 * начать с первой страницы, потому что страницы кэшированы в памяти.
 */
export function describeAdminBookingsError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return fallback
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  if (cause.hasCode('admin.bookings.cursor.invalid')) {
    return 'Список устарел. Обновите и начните с первой страницы.'
  }

  if (cause.status === 429) {
    return 'Слишком много запросов. Подождите минуту и повторите.'
  }

  return describeApiFailure(cause, fallback)
}

/**
 * Текст ошибки действия с бронью.
 *
 * Домен отвечает кодами: отмена уже завершённой брони, слишком ранняя неявка,
 * неявка для неподтверждённой брони. Показываем причину по-русски, а не
 * английский `detail`; на неизвестной ошибке — запасной текст экрана.
 */
export function describeAdminBookingActionError(
  cause: unknown,
  fallback: string
): string {
  if (!(cause instanceof ApiError)) {
    return fallback
  }

  if (cause.hasCode('booking.cancel.invalidStatus')) {
    return 'Бронь уже завершена или отменена.'
  }

  if (cause.hasCode('booking.markNoShow.tooEarly')) {
    return 'Неявку можно отметить только после начала визита.'
  }

  if (cause.hasCode('booking.markNoShow.invalidStatus')) {
    return 'Неявку отмечают только для подтверждённой брони.'
  }

  if (cause.status === 404 || cause.hasCode('admin.bookings.notFound')) {
    return 'Бронь не найдена.'
  }

  if (cause.status === 403 || cause.hasCode('auth.forbidden')) {
    return 'Раздел доступен только администратору.'
  }

  if (cause.status === 429) {
    return 'Слишком много запросов. Подождите минуту и повторите.'
  }

  return fallback
}
