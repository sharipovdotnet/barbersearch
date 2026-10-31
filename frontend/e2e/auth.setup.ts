import { existsSync } from 'node:fs'

import { expect, test as setup } from '@playwright/test'

import { accounts, baseURL, roles, storageStatePath } from './fixtures/accounts'

/**
 * Вход через API, а не через UI: `storageState` на роль готовится один раз,
 * UI-вход проверяется отдельным сценарием (§3.5).
 *
 * Порядок как у клиента: анонимный `GET /api/auth/csrf` → antiforgery-cookie,
 * затем `POST /api/auth/login/cookie` с заголовком `X-CSRF-TOKEN` (§9.4).
 *
 * Живой storageState переиспользуется: вход ограничен 10 запросами в минуту
 * (`AuthPermitLimit`), а прогон подряд не должен упираться в 429.
 */
for (const role of roles) {
  setup(`вход через API: ${role}`, async ({ playwright }) => {
    const account = accounts[role]
    const storageState = storageStatePath(role)

    if (existsSync(storageState)) {
      const existing = await playwright.request.newContext({ baseURL, storageState })

      try {
        const me = await existing.get('/api/auth/me')

        if (me.status() === 200) {
          return
        }
      } finally {
        await existing.dispose()
      }
    }

    const context = await playwright.request.newContext({ baseURL })

    const csrfResponse = await context.get('/api/auth/csrf')
    expect(csrfResponse.status(), 'GET /api/auth/csrf').toBe(200)

    const { token } = (await csrfResponse.json()) as { token: string }

    const loginResponse = await context.post('/api/auth/login/cookie', {
      headers: { 'X-CSRF-TOKEN': token },
      data: {
        email: account.email,
        password: account.password,
        rememberMe: false,
      },
    })

    expect(
      loginResponse.status(),
      `POST /api/auth/login/cookie для ${account.email}. ` +
        'Проверь, что база засеяна: npm run test:e2e:seed'
    ).toBe(204)

    const me = await context.get('/api/auth/me')
    expect(me.status(), 'GET /api/auth/me после входа').toBe(200)

    await context.storageState({ path: storageState })
    await context.dispose()
  })
}
