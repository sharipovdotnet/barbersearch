import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/account/profile` — личные данные кабинета.
 *
 * Форма живёт в каркасе, поэтому имя в рейле обновляется сразу после сохранения:
 * это и проверяет спека, а не только поля.
 */
export class AccountProfilePage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/account/profile')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Профиль', level: 1 })
  }

  get firstName(): Locator {
    return this.page.getByLabel('Имя')
  }

  get lastName(): Locator {
    return this.page.getByLabel('Фамилия')
  }

  get phone(): Locator {
    return this.page.getByLabel('Телефон')
  }

  get save(): Locator {
    return this.page.getByRole('button', { name: 'Сохранить' })
  }

  get status(): Locator {
    return this.page.getByRole('status')
  }

  get identity(): Locator {
    return this.page.locator('aside')
  }

  /** Ждём профиль, а не скелетон: поле имени заполнено из API. */
  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.firstName).toHaveValue(/\S/)
  }
}
