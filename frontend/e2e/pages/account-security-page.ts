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

  get passwordDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.changePassword).toBeVisible()
    await expect(this.signOut).toBeVisible()
  }
}
