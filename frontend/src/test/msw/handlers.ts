import { http, HttpResponse } from 'msw'

import type { BarberProfile } from '@/lib/catalog'

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

/**
 * Свой профиль барбера для кабинета: неподтверждённый (чтение работает до
 * верификации), с активной и скрытой услугой — так видно оба состояния прайса.
 */
export const testBarberProfile: BarberProfile = {
  id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  userId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  displayName: 'Иван Петров',
  grade: 'SeniorBarber',
  bio: 'Стригу с 2015 года.',
  yearsOfExperience: 10,
  languages: ['Ru', 'En'],
  certificatesAndTraining: ['Курс барберинга'],
  rating: 4.9,
  reviewsCount: 12,
  achievements: [],
  paymentMethods: ['Cash', 'Card'],
  communicationFormat: ['В салоне'],
  goodies: [],
  additionalServices: ['Укладка'],
  latePolicy: 'Жду максимум 10 минут.',
  isActive: true,
  isVerified: false,
  services: [
    {
      barberServiceId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
      hairServiceId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
      name: 'Стрижка',
      category: 'Haircut',
      defaultDurationMinutes: 45,
      price: 1500,
      durationMinutes: 45,
      isActive: true,
    },
    {
      barberServiceId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee',
      hairServiceId: 'ffffffff-ffff-4fff-8fff-ffffffffffff',
      name: 'Оформление бороды',
      category: 'Beard',
      defaultDurationMinutes: 30,
      price: 800,
      durationMinutes: 30,
      isActive: false,
    },
  ],
  salons: [],
  specializations: [
    {
      id: '11111111-2222-4333-8444-555555555555',
      specializationId: '66666666-7777-4888-8999-aaaaaaaaaaaa',
      name: 'Барберинг',
      description: null,
    },
  ],
}

/** Строка справочника услуг: `defaultDurationMinutes` — стартовое значение диалога. */
export const catalogueHairService = {
  id: '99999999-9999-4999-8999-999999999999',
  name: 'Стрижка',
  category: 'Haircut',
  defaultDurationMinutes: 45,
} as const

/** Строка справочника специализаций: описание может отсутствовать. */
export const catalogueSpecialization = {
  id: '77777777-7777-4777-8777-777777777777',
  name: 'Барберинг',
  description: null,
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
  // Восстановление забытого пароля: оба анонимных запроса по умолчанию успешны.
  // Сценарии `409 unavailable`, `429`, `400 invalid` и коды политики собираются
  // в тестах через `server.use`.
  http.post('/api/auth/password-reset', () => noContent()),
  http.post('/api/auth/password-reset/confirm', () => noContent()),
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
  // Свой профиль барбера и его прайс: чтение отдаёт и скрытые услуги, мутации
  // отвечают `204`. Сценарии отказа собираются в тестах через `server.use`.
  http.get('/api/barber-profiles/:id', () =>
    HttpResponse.json(testBarberProfile)
  ),
  http.put('/api/barber-profiles/:id', () => noContent()),
  http.post('/api/barber-profiles/:id/services', () => noContent()),
  http.put('/api/barber-profiles/:id/services/:hairServiceId', () =>
    noContent()
  ),
  http.delete('/api/barber-profiles/:id/services/:hairServiceId', () =>
    noContent()
  ),
  http.post(
    '/api/barber-profiles/:id/specializations/:specializationId',
    () => noContent()
  ),
  http.delete(
    '/api/barber-profiles/:id/specializations/:specializationId',
    () => noContent()
  ),
  // Публичные справочники для диалогов профиля: маленькие, без пагинации.
  http.get('/api/hair-services', () =>
    HttpResponse.json([catalogueHairService])
  ),
  http.get('/api/specializations', () =>
    HttpResponse.json([catalogueSpecialization])
  ),
]
