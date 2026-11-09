import { expect, type Locator, type Page } from '@playwright/test'

/** `/account/security` — пароль, выход и подтверждение email. */
export class AccountSecurityPage {
  readonly page: Page
  readonly main: Locator

  constructor(page: Page) {
    this.page = page
    this.main = page.getByRole('main')
  }

  async goto(): Promise<void> {
    await this.page.goto('/account/security')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Безопасность', level: 1 })
  }

  get changePassword(): Locator {
    return this.main.getByRole('button', { name: 'Сменить пароль' })
  }

  get signOut(): Locator {
    return this.main.getByRole('button', { name: 'Выйти' })
  }

  get emailCard(): Locator {
    return this.main
      .locator('[data-slot="card"]')
      .filter({ hasText: 'Подтверждение email' })
  }

  /** Строка статуса карточки: «Адрес подтверждён.» / «Адрес не подтверждён.». */
  get emailStatus(): Locator {
    return this.emailCard.getByText(/^Адрес (не )?подтверждён\.$/)
  }

  get emailVerifiedStatus(): Locator {
    return this.emailCard.getByText('Адрес подтверждён.', { exact: true })
  }

  get emailUnverifiedStatus(): Locator {
    return this.emailCard.getByText('Адрес не подтверждён.', { exact: true })
  }

  get resendButton(): Locator {
    return this.emailCard.getByRole('button', { name: 'Отправить письмо ещё раз' })
  }

  /** Кнопка после нажатия: «Повторить через 60 с» и заблокирована. */
  get resendCooldown(): Locator {
    return this.emailCard.getByRole('button', { name: /^Повторить через \d+ с$/ })
  }

  /** `?verification=sent` — уведомление приземления после регистрации. */
  get verificationSentNotice(): Locator {
    return this.main.getByRole('status').filter({
      hasText: 'Письмо отправлено на',
    })
  }

  get passwordDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.changePassword).toBeVisible()
    await expect(this.signOut).toBeVisible()
  }

  /** Ждём загруженный профиль: до него карточка показывает только статус сессии. */
  async waitForEmailStatus(): Promise<void> {
    await expect(this.emailCard).toBeVisible()
    await expect(this.emailStatus).toBeVisible()
  }
}
