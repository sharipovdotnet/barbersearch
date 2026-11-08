import { http, HttpResponse } from 'msw'

/**
 * Базовые обработчики: состояние «анонимного посетителя».
 *
 * Сценарные ответы (`signedInUser`, `loginLocked`, …) собираются в самом тесте через
 * `server.use(...)`, общие обработчики не мутируются — иначе тесты влияют друг на друга.
 */

export const testUser = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'client@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
} as const

/** Настройки напоминаний по умолчанию: те же значения создаёт сервер. */
export const notificationSettings = {
  isEnabled: true,
  digestEnabled: true,
  digestTime: '08:00',
  leadMinutes: 30,
  telegramLinked: false,
  telegramLinkedAt: null,
  botConfigured: true,
} as const

/** ProblemDetails, который проходит строгий гард `isProblemDetails` из `lib/api.ts`. */
export function problemDetails(
  status: number,
  options: { detail?: string; errorCodes?: string[]; title?: string } = {}
) {
  return {
    type: `https://httpstatuses.io/${status}`,
    title: options.title ?? 'Ошибка запроса',
    status,
    detail: options.detail,
    errorCodes: options.errorCodes ?? [],
  }
}

/** `401 auth.required` — ответ `/auth/me` для анонимного посетителя. */
export function unauthorized() {
  return HttpResponse.json(
    problemDetails(401, {
      title: 'Unauthorized',
      detail: 'Authentication is required.',
      errorCodes: ['auth.required'],
    }),
    { status: 401 }
  )
}

/** `204` без тела: так отвечают login, logout и refresh. */
export function noContent() {
  return new HttpResponse(null, { status: 204 })
}

export const handlers = [
  http.get('/api/auth/me', () => unauthorized()),
  http.get('/api/auth/csrf', () => HttpResponse.json({ token: 'csrf-token' })),
  http.post('/api/auth/login/cookie', () => noContent()),
  http.post('/api/auth/logout', () => noContent()),
  http.post('/api/auth/refresh', () => noContent()),
  // Подтверждение адреса: по умолчанию оба запроса успешны. Сценарии отказа
  // (`400 invalid`, `409 cooldown`, `429`) собираются в тестах через `server.use`.
  http.post('/api/auth/email-verification', () => noContent()),
  http.post('/api/auth/email-verification/confirm', () => noContent()),
  // Онбординг читает заявку «Стать барбером» перед формой профиля. Без заявки
  // (роль выдана администратором вручную) имя и опыт остаются редактируемыми.
  http.get('/api/barber-requests/mine', () =>
    HttpResponse.json({ request: null })
  ),
  // Колокольчик уведомлений живёт в заголовке кабинета, поэтому его счётчик
  // запрашивается на каждой странице кабинета. По умолчанию — непрочитанных нет.
  http.get('/api/barber/notifications/unread-count', () =>
    HttpResponse.json({ count: 0 })
  ),
  http.get('/api/barber/notifications', () =>
    HttpResponse.json({ items: [], nextCursor: null })
  ),
  http.get('/api/barber/notification-settings', () =>
    HttpResponse.json(notificationSettings)
  ),
]
