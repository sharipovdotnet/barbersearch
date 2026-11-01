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
]
