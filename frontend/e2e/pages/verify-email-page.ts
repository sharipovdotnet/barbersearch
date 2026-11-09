import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/verify-email` — приземление ссылки подтверждения адреса.
 *
 * Страница проходит четыре состояния: `checking` (скелетон), `confirmed`,
 * `invalid` (400 от confirm) и `error` (сеть/5xx); без токена — «Ссылка неполная».
 * Токен после ответа вычищается из адреса (`replace`), поэтому состояние
 * проверяется по заголовку карточки, а не по query.
 */
export class VerifyEmailPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  /** `token` не передан — открываем состояние «Ссылка неполная». */
  async goto(token?: string): Promise<void> {
    const query =
      token === undefined
        ? ''
        : `?${new URLSearchParams({ token }).toString()}`

    await this.page.goto(`/verify-email${query}`)
  }

  get confirmedHeading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Адрес подтверждён',
      level: 1,
    })
  }

  get invalidHeading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Не удалось подтвердить адрес',
      level: 1,
    })
  }

  get missingTokenHeading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Ссылка неполная',
      level: 1,
    })
  }

  get invalidReason(): Locator {
    return this.page.getByText('Ссылка недействительна или устарела.', {
      exact: true,
    })
  }

  get homeLink(): Locator {
    return this.page.getByRole('link', { name: 'На главную' })
  }

  get cabinetLink(): Locator {
    return this.page.getByRole('link', { name: 'Перейти в кабинет' })
  }

  /**
   * Успех: заголовок и очищенный адрес. Токен не должен остаться в строке —
   * это часть контракта страницы, а не косметика.
   */
  async waitForConfirmed(): Promise<void> {
    await expect(this.confirmedHeading).toBeVisible()
    await expect(this.page).toHaveURL(/\/verify-email$/)
  }

  /** Ошибка ссылки: понятный текст вместо белого экрана. */
  async waitForInvalid(): Promise<void> {
    await expect(this.invalidHeading).toBeVisible()
    await expect(this.invalidReason).toBeVisible()
    await expect(this.page).toHaveURL(/\/verify-email$/)
  }
}
