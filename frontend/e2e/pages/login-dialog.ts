import { expect, type Locator, type Page } from '@playwright/test'

import { accounts } from '../fixtures/accounts'

/** Диалог входа: шапка и гейты открывают его одним и тем же триггером. */
export class LoginDialog {
  readonly page: Page
  readonly dialog: Locator

  constructor(page: Page) {
    this.page = page
    this.dialog = page.getByRole('dialog')
  }

  async open(): Promise<void> {
    await this.page.getByRole('button', { name: 'Войти' }).first().click()
    await expect(this.dialog).toBeVisible()
  }

  get heading(): Locator {
    return this.dialog.getByRole('heading', { name: 'Вход в BarberSearch' })
  }

  get email(): Locator {
    return this.dialog.getByLabel('Email')
  }

  get password(): Locator {
    return this.dialog.getByLabel('Пароль')
  }

  get submit(): Locator {
    return this.dialog.getByRole('button', { name: 'Войти' })
  }

  /** Ошибка поля (`FieldError` = `role="alert"`) или общая ошибка формы. */
  error(text: string | RegExp): Locator {
    return this.dialog.getByText(text).first()
  }

  async fill(email: string, password: string): Promise<void> {
    await this.email.fill(email)
    await this.password.fill(password)
  }

  async submitWith(email: string, password: string): Promise<void> {
    await this.fill(email, password)
    await this.submit.click()
  }

  async signInAs(role: keyof typeof accounts): Promise<void> {
    const account = accounts[role]
    await this.submitWith(account.email, account.password)
  }

  async expectClosed(): Promise<void> {
    await expect(this.dialog).toBeHidden()
  }
}
