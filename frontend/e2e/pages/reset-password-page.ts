import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/reset-password` — установка нового пароля по ссылке из письма.
 *
 * Четыре состояния: `missing` (в адресе нет токена), `form`, `success` и
 * `invalid` (`400 auth.passwordReset.invalid`). Токен читается один раз и сразу
 * исчезает из адреса через `replace`, поэтому каждое ожидание проверяет и
 * заголовок карточки, и чистый URL: токен не должен оставаться в истории.
 *
 * Preflight-проверки нет: форма показывается для любого непустого токена, а
 * недействительность выясняется только на submit.
 */
export class ResetPasswordPage {
  readonly page: Page
  readonly main: Locator

  constructor(page: Page) {
    this.page = page
    this.main = page.getByRole('main')
  }

  /** `token` не передан — открываем состояние «Ссылка неполная». */
  async goto(token?: string): Promise<void> {
    const query =
      token === undefined ? '' : `?${new URLSearchParams({ token }).toString()}`

    await this.page.goto(`/reset-password${query}`)
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Новый пароль', level: 1 })
  }

  get missingHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Ссылка неполная', level: 1 })
  }

  get successHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Пароль изменён', level: 1 })
  }

  get invalidHeading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Ссылка недействительна',
      level: 1,
    })
  }

  get invalidReason(): Locator {
    return this.page.getByText(
      'Ссылка для сброса пароля устарела или уже использована. Запросите новую.',
      { exact: true }
    )
  }

  get password(): Locator {
    return this.page.getByLabel('Новый пароль', { exact: true })
  }

  get confirmation(): Locator {
    return this.page.getByLabel('Повторите пароль')
  }

  get submit(): Locator {
    return this.page.getByRole('button', { name: 'Сохранить новый пароль' })
  }

  /** Подсказка под полем: длина и запрет совпадения с email/именем. */
  get passwordHint(): Locator {
    return this.page.getByText(/^Минимум \d+ символов\./)
  }

  get successDescription(): Locator {
    return this.page.getByText(
      'Войдите с новым паролем. Все сессии входа завершены.',
      { exact: true }
    )
  }

  /** Кнопка-триггер `LoginDialog` на карточке успеха. */
  get loginButton(): Locator {
    return this.main.getByRole('button', { name: 'Войти' })
  }

  get homeLink(): Locator {
    return this.page.getByRole('link', { name: 'На главную' })
  }

  get requestNewLink(): Locator {
    return this.page.getByRole('link', { name: 'Запросить новую ссылку' })
  }

  /** Ошибка сервера/сети на форме (`role="alert"`). */
  get formError(): Locator {
    return this.page.getByRole('alert')
  }

  async waitForForm(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.password).toBeVisible()
    await expect(this.submit).toBeVisible()
    await expect(this.page).toHaveURL(/\/reset-password$/)
  }

  async waitForSuccess(): Promise<void> {
    await expect(this.successHeading).toBeVisible()
    await expect(this.successDescription).toBeVisible()
    await expect(this.page).toHaveURL(/\/reset-password$/)
  }

  async waitForInvalid(): Promise<void> {
    await expect(this.invalidHeading).toBeVisible()
    await expect(this.invalidReason).toBeVisible()
    await expect(this.page).toHaveURL(/\/reset-password$/)
  }

  async waitForMissing(): Promise<void> {
    await expect(this.missingHeading).toBeVisible()
    await expect(this.page).toHaveURL(/\/reset-password$/)
  }

  async fill(password: string, confirmation = password): Promise<void> {
    await this.password.fill(password)
    await this.confirmation.fill(confirmation)
  }

  async fillAndSubmit(password: string, confirmation = password): Promise<void> {
    await this.fill(password, confirmation)
    await this.submit.click()
  }

  /** Открывает `LoginDialog` с карточки успеха, не уходя со страницы. */
  async openLogin(): Promise<void> {
    await this.loginButton.click()
    await expect(this.page.getByRole('dialog')).toBeVisible()
  }
}
