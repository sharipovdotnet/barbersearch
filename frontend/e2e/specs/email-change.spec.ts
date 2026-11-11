import { expect, test, type Page } from '../fixtures/test'

import { expectNoBlockingViolations } from '../fixtures/axe'
import {
  EMAIL_CHANGE_NOTICE_SUBJECT,
  EMAIL_CHANGE_SUBJECT,
  extractEmailChangeToken,
  waitForEmailChangeMessage,
  waitForEmailChangeNoticeMessage,
} from '../fixtures/mail'
import { AccountProfilePage } from '../pages/account-profile-page'
import { ConfirmEmailChangePage } from '../pages/confirm-email-change-page'
import { LoginDialog } from '../pages/login-dialog'

/**
 * Смена email целиком: регистрация → заявка в `/account/profile` → два письма в
 * Mailpit → ссылка → вход новым адресом и отказ старому.
 *
 * Токен не хардкодится и база не читается: оба адреса уникальны для теста, письма
 * ищутся в настоящем инбоксе Mailpit по `To` **и теме**, потому что на старый
 * адрес уже пришло письмо подтверждения email, а на новый — уведомление о смене.
 * Поэтому сцена не зависит от порядка запуска и от писем прошлых прогонов.
 *
 * Проверка «старая сессия больше не действует» на уровне API живёт в
 * integration-тестах backend: здесь достаточно, что локальная сессия завершена и
 * вход старым адресом больше не проходит.
 */

/**
 * Только desktop. Сценарий делает несколько auth-запросов (регистрация с
 * авто-входом, `email-change`, `confirm`, проверка сессии, два входа), а лимит —
 * 10 запросов в минуту на IP; дублирование в mobile вместе с параллельными
 * спеками упирается в `429`. Лимит не ослабляется: 320 px покрыт axe-сканом
 * страницы в `a11y-narrow.spec.ts`.
 */
test.skip(
  () => test.info().project.name === 'mobile-chromium',
  'auth-лимит 10 запросов/мин на IP: сценарий делает несколько auth-запросов, ' +
    'а 320 px покрыт axe-сканом страницы (a11y-narrow.spec.ts). Лимит не ослабляется.'
)

/** Пароль проходит политику и не совпадает с email или именем. */
const PASSWORD = 'e2e-email-change-pass-2026'

/** Уникальный адрес: `e2e+<timestamp>-<project>-<random>@barbersearch.test`. */
function uniqueAddress(projectName: string, purpose: string): string {
  const project = projectName.replace(/[^a-z0-9]+/gi, '-')
  const timestamp = Date.now()
  const random = Math.random().toString(36).slice(2, 8)

  return `e2e+${purpose}-${timestamp}-${project}-${random}@barbersearch.test`
}

/** Регистрация через настоящую форму: она же выполняет авто-вход. */
async function registerCustomer(page: Page, email: string): Promise<void> {
  await page.goto('/register')
  await expect(
    page.getByRole('heading', { name: 'Регистрация', level: 1 })
  ).toBeVisible()

  await page.getByLabel('Имя').fill('E2E')
  await page.getByLabel('Фамилия').fill('Смена адреса')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill(PASSWORD)
  await page.getByLabel('Повтор пароля').fill(PASSWORD)
  await page.getByRole('checkbox', { name: /Я согласен/ }).click()

  await page.getByRole('button', { name: 'Зарегистрироваться' }).click()

  // Приземление после регистрации: карточка подтверждения адреса, а не /account.
  await expect(page).toHaveURL(/\/account\/security$/)
}

test.describe('смена email', () => {
  // Сценарий начинается анонимно: регистрация идёт через форму, но сессия
  // авто-входа снимается до первого шага.
  test.use({ storageState: { cookies: [], origins: [] } })

  test('заявка → два письма → ссылка → все сессии завершены, вход только новым адресом', async ({
    page,
  }, testInfo) => {
    const oldEmail = uniqueAddress(testInfo.project.name, 'change-old')
    const newEmail = uniqueAddress(testInfo.project.name, 'change-new')

    const profile = new AccountProfilePage(page)
    const confirm = new ConfirmEmailChangePage(page)

    await test.step('регистрация и карточка Email с неподтверждённым адресом', async () => {
      await registerCustomer(page, oldEmail)

      await profile.goto()
      await profile.waitForReady()

      await expect(profile.emailSection).toContainText(oldEmail)
      await expect(profile.emailStatus).toHaveText('Адрес не подтверждён.')
      await expect(profile.changeEmailButton).toBeEnabled()
    })

    await test.step('диалог доступен с клавиатуры и без нарушений axe @1280', async () => {
      await profile.openEmailChangeDialog()
      await expect(profile.newEmailInput).toBeVisible()
      await expect(profile.currentPasswordInput).toBeVisible()

      // Диалог — отдельное состояние страницы; 320 px в mobile-проекте
      // не сканируется (мутирующая сцена desktop-only).
      if ((page.viewportSize()?.width ?? 0) >= 1280) {
        await expectNoBlockingViolations(
          page,
          '/account/profile (диалог смены email) @1280',
          testInfo
        )
      }

      await page.keyboard.press('Escape')
      await expect(profile.emailDialog).toBeHidden()
      await expect(profile.changeEmailButton).toBeFocused()
    })

    await test.step('заявка отправляет письма и включает кулдаун', async () => {
      await profile.openEmailChangeDialog()
      await profile.requestEmailChange(newEmail, PASSWORD)

      await expect(profile.emailDialog).toBeHidden()
      await expect(profile.emailChangeStatus).toHaveText(
        `Письмо отправлено на ${newEmail}`
      )
      // Кнопка остаётся фокусируемой (aria-disabled), но повтор недоступен.
      await expect(profile.changeEmailButton).toHaveAttribute(
        'aria-disabled',
        'true'
      )
      await expect(profile.changeEmailButton).toHaveAccessibleName(
        /^Повторить через \d+ с$/
      )
    })

    const confirmation = await test.step('письмо со ссылкой приходит на новый адрес', async () => {
      const message = await waitForEmailChangeMessage(newEmail)

      expect(message.Subject).toBe(EMAIL_CHANGE_SUBJECT)
      expect(message.To.map((recipient) => recipient.Address)).toContain(newEmail)

      return message
    })

    const token = extractEmailChangeToken(confirmation)

    expect(token).toMatch(/^[A-Za-z0-9_-]{43}$/)

    await test.step('уведомление приходит на старый адрес и не содержит ссылки', async () => {
      const notice = await waitForEmailChangeNoticeMessage(oldEmail)

      expect(notice.Subject).toBe(EMAIL_CHANGE_NOTICE_SUBJECT)
      expect(notice.To.map((recipient) => recipient.Address)).toContain(oldEmail)
      expect(notice.Text).toContain(newEmail)

      // В уведомлении нет ни токена, ни ссылки подтверждения: оно только
      // сообщает о заявке и не является вторым каналом подтверждения.
      for (const body of [notice.HTML, notice.Text]) {
        expect(body).not.toContain('confirm-email-change')
      }
    })

    await test.step('ссылка меняет адрес и завершает локальную сессию', async () => {
      await confirm.goto(token)
      await confirm.waitForChanged()
      await expect(confirm.homeLink).toBeVisible()

      // Старая cookie больше не проходит проверку security stamp (или уже
      // удалена страницей): для клиента это аноним.
      const me = await page.request.get('/api/auth/me')

      expect(me.status()).toBe(401)
    })

    await test.step('старый адрес больше не входит, новый — входит с прежним паролем', async () => {
      const login = new LoginDialog(page)

      // После подтверждения все сессии завершены, поэтому вход — только заново.
      await confirm.loginButton.click()
      await expect(login.dialog).toBeVisible()

      await login.submitWith(oldEmail, PASSWORD)
      await expect(
        login.dialog.getByText('Неверный email или пароль.')
      ).toBeVisible()

      await login.submitWith(newEmail, PASSWORD)
      await login.expectClosed()
    })

    await test.step('кабинет показывает новый подтверждённый адрес', async () => {
      await profile.goto()
      await profile.waitForReady()

      await expect(profile.emailSection).toContainText(newEmail)
      await expect(profile.emailStatus).toHaveText('Адрес подтверждён.')
      await expect(profile.emailSection).not.toContainText(oldEmail)
    })
  })

  test('битый токен показывает понятную ошибку, а не белый экран', async ({
    page,
  }) => {
    const confirm = new ConfirmEmailChangePage(page)

    await confirm.goto('broken-token-from-e2e')

    await confirm.waitForInvalid()
    await expect(confirm.homeLink).toBeVisible()
    await expect(
      page.getByText(
        'Ссылка для смены email устарела или уже использована. Запросите новую в профиле.'
      )
    ).toBeVisible()
    await expect(confirm.changedHeading).toHaveCount(0)
  })
})
