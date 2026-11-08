import { request } from '@playwright/test'

/** Адрес API напрямую: `/health/*` живёт в корне приложения, а не под `/api`. */
const apiURL = process.env.E2E_API_URL ?? 'http://localhost:5243'

/** Mailpit: письма подтверждения email читаются из его HTTP API. */
const mailpitURL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'

/** Сколько ждать Mailpit: контейнер уже стартует, но не мгновенно. */
const MAILPIT_TIMEOUT_MS = 15_000

const API_HINT = [
  'API не отвечает на /health/ready.',
  'Подними бэкенд на http://localhost:5243 и базу barbersearch_e2e (README, раздел «E2E»):',
  '  dotnet run --project ..\\backend\\BarberSearch\\src\\BarberSearch.Web --launch-profile http',
].join('\n  ')

const MAILPIT_HINT = [
  `Mailpit не отвечает на ${mailpitURL}/api/v1/info.`,
  'Подними контейнер (README, раздел «E2E»):',
  '  docker run -d --rm --name barbersearch-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.21.8',
  'Письма подтверждения email не мокаются: без Mailpit падает email-verification.spec.ts.',
].join('\n  ')

/**
 * Проверяет готовность API и Mailpit до запуска тестов.
 *
 * Молчаливый skip запрещён (§3.4): без API все спеки падают одинаково и непонятно,
 * поэтому падаем сразу и с инструкцией. Mailpit ждём коротким polling'ом — иначе
 * первый тест отправляет письмо раньше, чем контейнер поднял SMTP-порт.
 */
export default async function globalSetup() {
  await waitForApi()
  await waitForMailpit()
}

async function waitForApi() {
  const context = await request.newContext()

  try {
    const response = await context.get(`${apiURL}/health/ready`, {
      timeout: 15_000,
    })

    if (!response.ok()) {
      throw new Error(
        `${API_HINT}\n  Ответ: HTTP ${response.status()}. Проверь строку подключения и миграции.`
      )
    }
  } catch (error) {
    if (error instanceof Error && error.message.startsWith('API не отвечает')) {
      throw error
    }

    throw new Error(
      `${API_HINT}\n  Причина: ${error instanceof Error ? error.message : String(error)}`
    )
  } finally {
    await context.dispose()
  }
}

async function waitForMailpit() {
  const context = await request.newContext()
  const deadline = Date.now() + MAILPIT_TIMEOUT_MS
  let lastAnswer = 'нет ответа'

  try {
    while (Date.now() < deadline) {
      try {
        const response = await context.get(`${mailpitURL}/api/v1/info`, {
          timeout: 2_000,
        })

        if (response.ok()) {
          return
        }

        lastAnswer = `HTTP ${response.status()}`
      } catch (error) {
        lastAnswer =
          error instanceof Error ? error.message : String(error)
      }

      await new Promise((resolve) => {
        setTimeout(resolve, 250)
      })
    }

    throw new Error(`${MAILPIT_HINT}\n  Ответ: ${lastAnswer}.`)
  } finally {
    await context.dispose()
  }
}
