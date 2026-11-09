import { expect, test, type Page } from '../fixtures/test'

import { expectNoBlockingViolations } from '../fixtures/axe'
import {
  extractPasswordResetToken,
  PASSWORD_RESET_SUBJECT,
  waitForPasswordResetMessage,
} from '../fixtures/mail'
import { ForgotPasswordPage } from '../pages/forgot-password-page'
import { LoginDialog } from '../pages/login-dialog'
import { ResetPasswordPage } from '../pages/reset-password-page'

/**
 * Сброс забытого пароля целиком: регистрация → `/forgot-password` → письмо в
 * Mailpit → `/reset-password` → вход новым паролем → повторное использование
 * ссылки.
 *
 * Токен не хардкодится и база не читается: адрес уникален для теста, письмо
 * ищется в настоящем инбоксе Mailpit по `To` **и теме**, потому что на этот же
 * адрес уже пришло письмо подтверждения email. Поэтому сцена не зависит от
 * порядка запуска и от писем прошлых прогонов.
 *
 * Проверка «старый пароль больше не входит» живёт в integration-тестах backend:
 * лишний auth-запрос здесь только приближает `429`. Анти-энумерация (одинаковый
 * экран для неизвестного адреса) — тоже не дублируется: её держат application,
 * integration и unit-тесты страницы.
 */

/**
 * Только desktop. Сценарий делает несколько auth-запросов (регистрация с
 * авто-входом, `password-reset`, два `confirm`, вход — шесть), а лимит — 10
 * запросов в минуту на IP; дублирование в mobile вместе с параллельными спеками
 * упирается в `429`. Лимит не ослабляется: 320 px покрыт axe-сканом формы в
 * `a11y-narrow.spec.ts`.
 */
test.skip(
  () => test.info().project.name === 'mobile-chromium',
  'auth-лимит 10 запросов/мин на IP: сценарий делает несколько auth-запросов, ' +
    'а 320 px покрыт axe-сканом формы (a11y-narrow.spec.ts). Лимит не ослабляется.'
)

/** Пароль до сброса; в E2E им только регистрируемся. */
const OLD_PASSWORD = 'e2e-reset-old-pass-2026'
/** Пароль после сброса: им входим из `LoginDialog`. */
const NEW_PASSWORD = 'e2e-reset-new-pass-2026'

/** Уникальный адрес: `e2e+<timestamp>-<project>-<random>@barbersearch.test`. */
function uniqueAddress(projectName: string): string {
  const project = projectName.replace(/[^a-z0-9]+/gi, '-')
  const timestamp = Date.now()
  const random = Math.random().toString(36).slice(2, 8)

  return `e2e+${timestamp}-${project}-${random}@barbersearch.test`
}

/** Регистрация через настоящую форму: она же выполняет авто-вход. */
async function registerCustomer(
  page: Page,
  email: string,
  password: string
): Promise<void> {
  await page.goto('/register')
  await expect(
    page.getByRole('heading', { name: 'Регистрация', level: 1 })
  ).toBeVisible()

  await page.getByLabel('Имя').fill('E2E')
  await page.getByLabel('Фамилия').fill('Сброс')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill(password)
  await page.getByLabel('Повтор пароля').fill(password)
  await page.getByRole('checkbox', { name: /Я согласен/ }).click()

  await page.getByRole('button', { name: 'Зарегистрироваться' }).click()

  // Приземление после регистрации: карточка подтверждения адреса, а не /account.
  await expect(page).toHaveURL(/\/account\/security$/)
}

test.describe('сброс пароля', () => {
  // Сценарий начинается анонимно: регистрация идёт через форму, но сессия
  // авто-входа снимается до первого шага сброса.
  test.use({ storageState: { cookies: [], origins: [] } })

  test('письмо со ссылкой → новый пароль → вход новым; вторая попытка недействительна', async ({
    page,
    context,
  }, testInfo) => {
    const email = uniqueAddress(testInfo.project.name)

    const forgot = new ForgotPasswordPage(page)
    const reset = new ResetPasswordPage(page)

    await test.step('регистрация с паролем OLD и анонимный старт', async () => {
      await registerCustomer(page, email, OLD_PASSWORD)

      // Авто-вход после регистрации больше не нужен: сценарий просит ссылку
      // анонимно, как это делает потерявший пароль человек.
      await context.clearCookies()

      await forgot.goto()
      await forgot.waitForReady()
    })

    await test.step('запрос ссылки даёт generic-экран «Проверьте почту»', async () => {
      await forgot.requestLink(email)
      await forgot.waitForSent(email)

      // Generic-текст не различает существующий и несуществующий адрес.
      await expect(forgot.sentDescription).toContainText(
        'Если аккаунт с адресом'
      )
      await expect(forgot.homeLink).toBeVisible()
      await expect(forgot.anotherAddress).toBeVisible()

      // Локальный кулдаун повторной отправки: 60 секунд, кнопка недоступна.
      await expect(forgot.resendButton).toHaveText(/^Повторить через \d+ с$/)
      await expect(forgot.resendButton).toBeDisabled()
    })

    const message = await test.step('в Mailpit ждём письмо по теме, а не первое на адрес', async () => {
      // На адрес уже пришло письмо подтверждения email: без фильтра по теме
      // `waitForMessageTo` вернул бы его и сломал сценарий.
      const received = await waitForPasswordResetMessage(email)

      expect(received.Subject).toBe(PASSWORD_RESET_SUBJECT)
      expect(received.To.map((recipient) => recipient.Address)).toContain(email)

      return received
    })

    const token = extractPasswordResetToken(message)

    expect(token).toMatch(/^[A-Za-z0-9_-]{43}$/)

    await test.step('ссылка открывает форму и сразу убирает токен из адреса', async () => {
      await reset.goto(token)
      await reset.waitForForm()

      // `waitForForm` уже проверяет чистый URL: токен не остаётся в истории.
      await expect(reset.passwordHint).toBeVisible()
    })

    await test.step('новый пароль сохранён', async () => {
      await reset.fillAndSubmit(NEW_PASSWORD)
      await reset.waitForSuccess()

      await expect(reset.loginButton).toBeVisible()
      await expect(reset.homeLink).toBeVisible()
    })

    await test.step('axe: карточка успеха на 1280', async () => {
      // Скан до входа: после успешного входа `LoginDialog` уводит на `/account`
      // и карточка успеха исчезает. 320 px живёт в `a11y-narrow.spec.ts`.
      await expectNoBlockingViolations(
        page,
        '/reset-password (пароль изменён) @1280',
        testInfo
      )
    })

    await test.step('вход новым паролем с карточки успеха ведёт в /account', async () => {
      await reset.openLogin()

      const login = new LoginDialog(page)

      await expect(login.heading).toBeVisible()
      await login.submitWith(email, NEW_PASSWORD)
      await expect(login.dialog).toBeHidden()

      await expect(page).toHaveURL(/\/account$/)
      await expect(
        page.getByRole('heading', { name: 'Мои записи', level: 1 })
      ).toBeVisible()
    })

    await test.step('использованная ссылка: форма появляется, submit — «Ссылка недействительна»', async () => {
      // Preflight-проверки токена нет: страница показывает форму для любого
      // непустого токена, а недействительность выясняется на submit.
      await reset.goto(token)
      await reset.waitForForm()

      await reset.fillAndSubmit(NEW_PASSWORD)
      await reset.waitForInvalid()

      // Успеха нет: токен одноразовый, повторный confirm — `400 invalid`.
      await expect(reset.successHeading).toHaveCount(0)
      await expect(reset.requestNewLink).toBeVisible()
      await expect(reset.homeLink).toBeVisible()
    })
  })
})
