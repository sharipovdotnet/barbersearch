import { t } from '@/i18n'
import { ApiError, apiRequest } from '@/lib/api'
import { mutateWithCsrf } from '@/lib/csrf'
import { describeApiFailure } from '@/lib/errors'
import type { Page } from '@/lib/paging'
import {
  isBarberNotificationPage,
  isNotificationSettings,
  isTelegramLink,
  isUnreadNotificationsPayload,
} from '@/lib/response-guards'

/**
 * Инбокс напоминаний барбера и его настройки.
 *
 * Это единственный обязательный канал доставки (D1): Telegram — необязательный
 * транспорт поверх тех же строк. Список курсорный, счётчик непрочитанных — отдельный
 * дешёвый запрос для колокольчика, настройки — один `GET` и одна `PUT` на изменение.
 *
 * Контракт `NotificationSettingsResponse` сознательно не содержит `chatId`: клиенту
 * достаточно `telegramLinked` и `botConfigured`, а идентификатор чата — не его данные.
 */

export type BarberNotificationKind = 'Upcoming' | 'Digest'

export type BarberNotificationDelivery = 'Pending' | 'Sent' | 'Skipped' | 'Failed'

export type BarberNotification = {
  id: string
  kind: BarberNotificationKind
  createdAt: string
  /** `null` — уведомление ещё не прочитано. */
  readAt: string | null
  /** Запись, о которой напоминание; `null` у дайджеста. */
  bookingId: string | null
  /** Начало визита; у дайджеста — начало первой записи дня. */
  startAt: string | null
  serviceName: string | null
  salonName: string | null
  /** IANA-пояс салона: время в строке показывается именно в нём. */
  salonTimeZoneId: string | null
  /** Число записей дня; `null` у напоминания о визите. */
  bookingsCount: number | null
  delivery: BarberNotificationDelivery
}

/** Только три значения: их же предлагает селект «Напомнить за». */
export type NotificationLeadMinutes = 15 | 30 | 60

export const NOTIFICATION_LEAD_MINUTES: readonly NotificationLeadMinutes[] = [
  15, 30, 60,
]

/** Сервер создаёт настройки с 30 минутами; до их загрузки показываем то же. */
export const DEFAULT_LEAD_MINUTES: NotificationLeadMinutes = 30

export type NotificationSettings = {
  isEnabled: boolean
  digestEnabled: boolean
  /** Строка `HH:mm`, готовая для `<input type="time">`. */
  digestTime: string
  leadMinutes: NotificationLeadMinutes
  telegramLinked: boolean
  telegramLinkedAt: string | null
  /** Настроен ли бот на сервере: без него кнопка «Подключить» бессмысленна. */
  botConfigured: boolean
}

/** Тело `PUT /api/barber/notification-settings`: только изменяемые поля. */
export type NotificationSettingsInput = Pick<
  NotificationSettings,
  'isEnabled' | 'digestEnabled' | 'digestTime' | 'leadMinutes'
>

export type TelegramLink = {
  /** Одноразовый код привязки; живёт 10 минут. */
  code: string
  /** `https://t.me/{bot}?start={code}`. */
  deepLink: string
  expiresAt: string
}

/** Двадцать уведомлений на страницу — дефолт сервера. */
export const BARBER_NOTIFICATIONS_PAGE_SIZE = 20

export async function fetchNotifications(
  cursor?: string | null,
  signal?: AbortSignal
): Promise<Page<BarberNotification>> {
  const query = new URLSearchParams({
    pageSize: String(BARBER_NOTIFICATIONS_PAGE_SIZE),
  })

  if (cursor) {
    query.set('cursor', cursor)
  }

  // Чтение без CSRF: токен нужен только мутациям.
  return apiRequest<Page<BarberNotification>>(
    `/barber/notifications?${query.toString()}`,
    signal ? { signal } : {},
    { validate: isBarberNotificationPage }
  )
}

/** `404 notification.notFound` — уведомление уже удалено, повтор не нужен. */
export async function markNotificationRead(id: string): Promise<void> {
  await mutateWithCsrf<void>(
    `/barber/notifications/${encodeURIComponent(id)}/read`,
    { method: 'POST' }
  )
}

/** Идемпотентно: повторный вызов не меняет уже прочитанные строки. */
export async function markAllRead(): Promise<void> {
  await mutateWithCsrf<void>('/barber/notifications/read-all', {
    method: 'POST',
  })
}

/** Счётчик для колокольчика; сервер ограничивает его сам, клиент рисует «99+». */
export async function fetchUnreadCount(
  signal?: AbortSignal
): Promise<{ count: number }> {
  return apiRequest<{ count: number }>(
    '/barber/notifications/unread-count',
    signal ? { signal } : {},
    { validate: isUnreadNotificationsPayload }
  )
}

export async function fetchSettings(
  signal?: AbortSignal
): Promise<NotificationSettings> {
  return apiRequest<NotificationSettings>(
    '/barber/notification-settings',
    signal ? { signal } : {},
    { validate: isNotificationSettings }
  )
}

/** Одна `PUT` на пачку изменений: дебаунс 500 мс живёт в хуке. */
export async function updateSettings(
  input: NotificationSettingsInput,
  signal?: AbortSignal
): Promise<NotificationSettings> {
  return mutateWithCsrf<NotificationSettings>('/barber/notification-settings', {
    method: 'PUT',
    body: JSON.stringify(input),
    ...(signal ? { signal } : {}),
    validate: isNotificationSettings,
  })
}

/** Код привязки: сервер хранит только его SHA-256 и отдаёт deep link. */
export async function createTelegramLink(): Promise<TelegramLink> {
  return mutateWithCsrf<TelegramLink>(
    '/barber/notifications/telegram/link',
    { method: 'POST', validate: isTelegramLink }
  )
}

/** Идемпотентно: отвязка уже отвязанного чата — не ошибка. */
export async function unlinkTelegram(): Promise<void> {
  await mutateWithCsrf<void>('/barber/notifications/telegram/unlink', {
    method: 'POST',
  })
}

export const NOTIFICATIONS_LOAD_FALLBACK = 'Не удалось загрузить уведомления.'
export const NOTIFICATION_SETTINGS_LOAD_FALLBACK =
  'Не удалось загрузить настройки.'
export const NOTIFICATION_SETTINGS_SAVE_FALLBACK =
  'Не удалось сохранить настройки.'

/** Ошибка списка или счётчика: общий русский фолбэк без английского `detail`. */
export function describeNotificationsError(
  error: unknown,
  fallback = NOTIFICATIONS_LOAD_FALLBACK
): string {
  return describeApiFailure(error, fallback)
}

export function describeNotificationSettingsLoadError(
  error: unknown
): string {
  if (error instanceof ApiError && error.status === 403) {
    return t('Настройки доступны только барберу.')
  }

  return describeApiFailure(error, NOTIFICATION_SETTINGS_LOAD_FALLBACK)
}

/** Поля, у которых сервер умеет вернуть `400` отдельным кодом. */
export type NotificationSettingsField = 'digestTime' | 'leadMinutes'

export type NotificationSettingsFailure = {
  /** `null` — ошибка формы, а не конкретного поля. */
  field: NotificationSettingsField | null
  message: string
}

/**
 * Ошибка сохранения настроек.
 *
 * `400 notificationSettings.leadMinutes` и `.digestTime` — единственные публичные коды
 * валидации полей; остальное (сеть, `429`, `403`) показывается общей строкой, чтобы
 * не приписывать ошибку полю, которого она не касается.
 */
export function describeNotificationSettingsFailure(
  error: unknown
): NotificationSettingsFailure {
  if (error instanceof ApiError) {
    if (error.hasCode('notificationSettings.leadMinutes')) {
      return {
        field: 'leadMinutes',
        message: t('Напомнить можно за 15, 30 или 60 минут.'),
      }
    }

    if (error.hasCode('notificationSettings.digestTime')) {
      return {
        field: 'digestTime',
        message: t('Время дайджеста — с 06:00 до 12:00, кратно 15 минутам.'),
      }
    }
  }

  return {
    field: null,
    message: describeApiFailure(error, NOTIFICATION_SETTINGS_SAVE_FALLBACK),
  }
}

/**
 * Ошибка получения кода привязки.
 *
 * `409 notification.telegram.alreadyLinked` значит, что чат привязали в другой
 * вкладке: поможет только повторное чтение настроек.
 */
export function describeTelegramLinkError(
  error: unknown,
  fallback = 'Не удалось подключить Telegram.'
): string {
  if (error instanceof ApiError) {
    if (
      error.status === 409 ||
      error.hasCode('notification.telegram.alreadyLinked')
    ) {
      return t('Telegram уже подключён. Обновите страницу.')
    }

    if (error.status === 403 || error.hasCode('auth.forbidden')) {
      return t('Подключать Telegram может только барбер.')
    }
  }

  return describeApiFailure(error, fallback)
}

export function describeTelegramUnlinkError(
  error: unknown,
  fallback = 'Не удалось отключить Telegram.'
): string {
  if (error instanceof ApiError && error.status === 403) {
    return t('Отключать Telegram может только барбер.')
  }

  return describeApiFailure(error, fallback)
}
