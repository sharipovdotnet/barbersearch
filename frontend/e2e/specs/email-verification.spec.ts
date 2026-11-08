import { expect, test, type Page } from '../fixtures/test'

import { expectNoBlockingViolations } from '../fixtures/axe'
import { extractVerificationToken, waitForMessageTo } from '../fixtures/mail'
import { AccountSecurityPage } from '../pages/account-security-page'
import { VerifyEmailPage } from '../pages/verify-email-page'

/**
 * Подтверждение email целиком: регистрация → письмо в Mailpit → ссылка →
 * подтверждённый кабинет.
 *
 * Токен не хардкодится и база не читается: адрес уникален для теста, письмо
 * ищется в настоящем инбоксе Mailpit по `To`, а ссылка берётся из тела письма.
 * Поэтому сцена не зависит от порядка запуска и от писем прошлых прогонов.
 */

/** Пароль проходит политику и не совпадает с email или именем. */
const PASSWORD = 'e2e-verification-pass-2026'
const PASSWORD_CONFIRMATION = PASSWORD

/** Уникальный адрес: `e2e+<timestamp>-<random>@barbersearch.test`. */
function uniqueAddress(projectName: string): string {
  const project = projectName.replace(/[^a-z0-9]+/gi, '-')
  const timestamp = Date.now()
  const random = Math.random().toString(36).slice(2, 8)

  return `e2e+${timestamp}-${project}-${random}@barbersearch.test`
}

/** Регистрация через настоящую форму: она же выполняет авто-вход. */
async function registerCustomer(page: Page, email: string): Promise<void> {
  await page.goto('/register')
  await expect(
    page.getByRole('heading', { name: 'Регистрация', level: 1 })
  ).toBeVisible()

  await page.getByLabel('Имя').fill('E2E')
  await page.getByLabel('Фамилия').fill('Подтверждение')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill(PASSWORD)
  await page.getByLabel('Повтор пароля').fill(PASSWORD_CONFIRMATION)
  await page
    .getByRole('checkbox', { name: /Я согласен/ })
    .click()

  await page.getByRole('button', { name: 'Зарегистрироваться' }).click()

  // Приземление после регистрации: карточка подтверждения адреса, а не /account.
  await expect(page).toHaveURL(/\/account\/security$/)
}

test.describe('подтверждение email', () => {
  test('регистрация → письмо → ссылка → зелёный кабинет, повторный клик тоже успешен', async ({
    page,
  }, testInfo) => {
    const email = uniqueAddress(testInfo.project.name)

    await test.step('регистрация ведёт в кабинет с неподтверждённым адресом', async () => {
      await registerCustomer(page, email)

      const security = new AccountSecurityPage(page)

      await security.waitForReady()
      await security.waitForEmailStatus()
      await expect(security.emailCard).toContainText(email)
      await expect(security.emailUnverifiedStatus).toBeVisible()
      await expect(security.resendButton).toBeVisible()
    })

    const message = await test.step('письмо приходит в Mailpit со ссылкой', async () => {
      const received = await waitForMessageTo(email)

      expect(received.Subject).toBe(
        'Подтвердите адрес электронной почты — BarberSearch'
      )
      expect(received.To.map((recipient) => recipient.Address)).toContain(email)

      return received
    })

    const token = extractVerificationToken(message)

    expect(token).toMatch(/^[A-Za-z0-9_-]{43}$/)

    await test.step('ссылка из письма подтверждает адрес', async () => {
      const verify = new VerifyEmailPage(page)

      await verify.goto(token)
      await verify.waitForConfirmed()
      await expect(verify.homeLink).toBeVisible()
      // Клейм обновился: значит, refreshSession/retrySession страницы отработали.
      await expect(verify.cabinetLink).toBeVisible()

      // 320 px живёт в `a11y-narrow.spec.ts`: здесь скан подтверждённого
      // состояния на 1280, в mobile-проекте он не дублируется.
      if ((page.viewportSize()?.width ?? 0) >= 1280) {
        await expectNoBlockingViolations(
          page,
          '/verify-email (подтверждён) @1280',
          testInfo
        )
      }
    })

    await test.step('кабинет показывает зелёный статус без кнопки переотправки', async () => {
      await page.goto('/account/security')

      const security = new AccountSecurityPage(page)

      await security.waitForReady()
      await security.waitForEmailStatus()
      await expect(security.emailVerifiedStatus).toBeVisible()
      await expect(security.emailUnverifiedStatus).toHaveCount(0)
      await expect(security.resendButton).toHaveCount(0)
      await expect(security.emailCard).toContainText(email)
    })

    await test.step('повторный переход по той же ссылке снова успешен', async () => {
      const verify = new VerifyEmailPage(page)

      await verify.goto(token)
      await verify.waitForConfirmed()
    })
  })

  test('битый токен показывает понятную ошибку, а не белый экран', async ({
    page,
  }) => {
    const verify = new VerifyEmailPage(page)

    await verify.goto('broken-token-from-e2e')

    await verify.waitForInvalid()
    await expect(verify.homeLink).toBeVisible()
    await expect(
      page.getByText(
        'Попробуйте ещё раз из письма или запросите новое в разделе «Безопасность».'
      )
    ).toBeVisible()
    await expect(verify.confirmedHeading).toHaveCount(0)
  })
})
