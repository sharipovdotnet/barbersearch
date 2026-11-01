import { request } from '@playwright/test'

/** Адрес API напрямую: `/health/*` живёт в корне приложения, а не под `/api`. */
const apiURL = process.env.E2E_API_URL ?? 'http://localhost:5243'

const HINT = [
  'API не отвечает на /health/ready.',
  'Подними бэкенд на http://localhost:5243 и базу barbersearch_e2e (README, раздел «E2E»):',
  '  dotnet run --project ..\\backend\\BarberSearch\\src\\BarberSearch.Web --launch-profile http',
].join('\n  ')

/**
 * Проверяет готовность API до запуска тестов.
 *
 * Молчаливый skip запрещён (§3.4): без API все спеки падают одинаково и непонятно,
 * поэтому падаем сразу и с инструкцией.
 */
export default async function globalSetup() {
  const context = await request.newContext()

  try {
    const response = await context.get(`${apiURL}/health/ready`, {
      timeout: 15_000,
    })

    if (!response.ok()) {
      throw new Error(
        `${HINT}\n  Ответ: HTTP ${response.status()}. Проверь строку подключения и миграции.`
      )
    }
  } catch (error) {
    if (error instanceof Error && error.message.startsWith('API не отвечает')) {
      throw error
    }

    throw new Error(
      `${HINT}\n  Причина: ${error instanceof Error ? error.message : String(error)}`
    )
  } finally {
    await context.dispose()
  }
}
