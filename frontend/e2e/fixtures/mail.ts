/**
 * Mailpit-клиент для E2E-сценариев с письмами.
 *
 * Письма подтверждения email не мокаются: тест читает настоящий инбокс Mailpit
 * (`GET /api/v1/messages`, затем `GET /api/v1/message/{id}`) и берёт из него
 * токен. Адрес уникален для теста, поэтому письма фильтруются по `To`, а не по
 * порядку в инбоксе: прогон не зависит от соседних сцен и прошлых запусков.
 *
 * Mailpit обязателен для всего E2E: `global-setup.ts` проверяет
 * `GET /api/v1/info` до старта тестов и падает с инструкцией, если контейнера нет.
 */

const MAILPIT_URL = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'

/** Сколько ждать письмо: SMTP-отправка синхронна, но не мгновенна. */
const MESSAGE_TIMEOUT_MS = 10_000

/** Интервал опроса инбокса, пока письма нет. */
const POLL_INTERVAL_MS = 250

/** Инбокс просматриваем целиком: лимит заведомо больше одного прогона. */
const MESSAGES_LIMIT = 200

/** Сколько ждать ответа Mailpit: зависший HTTP не должен съесть весь таймаут. */
const REQUEST_TIMEOUT_MS = 3_000

export type MailpitAddress = {
  Name: string
  Address: string
}

/** Строка списка писем: без тел, только конверт. */
export type MailpitMessageSummary = {
  ID: string
  Subject: string
  To: MailpitAddress[]
}

/** Письмо целиком: тела нужны, чтобы достать ссылку подтверждения. */
export type MailpitMessage = MailpitMessageSummary & {
  HTML: string
  Text: string
}

/**
 * Ждёт письмо для адреса и возвращает его целиком.
 *
 * Ошибка называет адрес и подсказывает, что проверить: без понятного текста
 * упавший первый тест выглядит как «просто не нашлось письмо».
 */
export async function waitForMessageTo(address: string): Promise<MailpitMessage> {
  const deadline = Date.now() + MESSAGE_TIMEOUT_MS
  let lastError: string | null = null

  while (Date.now() < deadline) {
    try {
      const summaries = await listMessages()
      const summary = summaries.find((message) => isAddressedTo(message, address))

      if (summary) {
        return await fetchMessage(summary.ID)
      }
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error)
    }

    await delay(POLL_INTERVAL_MS)
  }

  const details = lastError === null ? '' : `\n  Последняя ошибка: ${lastError}`

  throw new Error(
    [
      `Письмо для ${address} не пришло в Mailpit за ${MESSAGE_TIMEOUT_MS / 1000} с.`,
      `  Проверь Mailpit: docker run -d --rm --name barbersearch-mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.21.8`,
      `  и что API запущен с Email__Enabled=true, Email__Host=localhost, Email__Port=1025, Email__UseTls=false и App__PublicUrl=http://localhost:5173.`,
      `  Инбокс: ${MAILPIT_URL}`,
    ].join('\n') + details
  )
}

/**
 * Достаёт токен подтверждения из ссылки `/verify-email?token=…`.
 *
 * Ищем в HTML и в текстовом дубле: письмо содержит обе версии, и любая из них
 * — настоящий контракт письма. Токен — 43 символа base64url (§4.2).
 */
export function extractVerificationToken(message: MailpitMessage): string {
  const link = /\/verify-email\?token=([A-Za-z0-9_-]+)/

  for (const body of [message.HTML, message.Text]) {
    const match = link.exec(body)

    if (match?.[1]) {
      return decodeURIComponent(match[1])
    }
  }

  throw new Error(
    `В письме «${message.Subject}» нет ссылки /verify-email?token=…: проверь текст письма (EmailVerificationMessage).`
  )
}

/** Список писем инбокса, новые сверху. */
async function listMessages(): Promise<MailpitMessageSummary[]> {
  const response = await fetch(
    `${MAILPIT_URL}/api/v1/messages?limit=${MESSAGES_LIMIT}`,
    { signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) }
  )

  if (!response.ok) {
    throw new Error(`GET /api/v1/messages: HTTP ${response.status}`)
  }

  const body: unknown = await response.json()

  if (!isMessageList(body)) {
    throw new Error('GET /api/v1/messages: неожиданный ответ Mailpit')
  }

  return body.messages
}

/** Письмо с телами по id. */
async function fetchMessage(id: string): Promise<MailpitMessage> {
  const response = await fetch(
    `${MAILPIT_URL}/api/v1/message/${encodeURIComponent(id)}`,
    { signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS) }
  )

  if (!response.ok) {
    throw new Error(`GET /api/v1/message/${id}: HTTP ${response.status}`)
  }

  const body: unknown = await response.json()

  if (!isMessage(body)) {
    throw new Error(`GET /api/v1/message/${id}: неожиданный ответ Mailpit`)
  }

  return body
}

/** Получатель письма: Mailpit может отдать адрес в другом регистре. */
function isAddressedTo(
  message: MailpitMessageSummary,
  address: string
): boolean {
  const wanted = address.toLowerCase()

  return message.To.some((recipient) => recipient.Address.toLowerCase() === wanted)
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, milliseconds)
  })
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isAddress(value: unknown): value is MailpitAddress {
  return (
    isRecord(value) &&
    typeof value.Name === 'string' &&
    typeof value.Address === 'string'
  )
}

function isMessageSummary(value: unknown): value is MailpitMessageSummary {
  return (
    isRecord(value) &&
    typeof value.ID === 'string' &&
    typeof value.Subject === 'string' &&
    Array.isArray(value.To) &&
    value.To.every(isAddress)
  )
}

function isMessageList(
  value: unknown
): value is { messages: MailpitMessageSummary[] } {
  return (
    isRecord(value) &&
    Array.isArray(value.messages) &&
    value.messages.every(isMessageSummary)
  )
}

function isMessage(value: unknown): value is MailpitMessage {
  return (
    isRecord(value) &&
    typeof value.HTML === 'string' &&
    typeof value.Text === 'string' &&
    isMessageSummary(value)
  )
}
