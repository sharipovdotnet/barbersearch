import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/confirm-email-change` — приземление ссылки подтверждения нового адреса.
 *
 * Пять состояний: `checking` (скелетон), `changed`, `invalid` (400 от confirm),
 * `error` (сеть/`5xx`) и «Ссылка неполная» без токена. После ответа токен
 * вычищается из адреса (`replace`), поэтому состояние проверяется по заголовку
 * карточки, а не по query.
 */
export class ConfirmEmailChangePage {
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

    await this.page.goto(`/confirm-email-change${query}`)
  }

  get changedHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Email изменён', level: 1 })
  }

  get invalidHeading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Ссылка недействительна',
      level: 1,
    })
  }

  get errorHeading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Не удалось проверить ссылку',
      level: 1,
    })
  }

  get missingTokenHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Ссылка неполная', level: 1 })
  }

  get invalidReason(): Locator {
    return this.page.getByText('Ссылка недействительна или устарела.', {
      exact: true,
    })
  }

  /**
   * Триггер входа на успехе: гость после смены адреса обязан войти заново,
   * потому что подтверждение завершает все сессии. Ищем внутри `main`:
   * в шапке есть такой же триггер, и без области получаем strict violation.
   */
  get loginButton(): Locator {
    return this.page
      .getByRole('main')
      .getByRole('button', { name: 'Войти' })
  }

  get homeLink(): Locator {
    return this.page.getByRole('link', { name: 'На главную' })
  }

  /** Успех: заголовок, очищенный адрес и предложение войти заново. */
  async waitForChanged(): Promise<void> {
    await expect(this.changedHeading).toBeVisible()
    await expect(this.page).toHaveURL(/\/confirm-email-change$/)
    await expect(this.loginButton).toBeVisible()
  }

  /** Ошибка ссылки: понятный текст вместо белого экрана. */
  async waitForInvalid(): Promise<void> {
    await expect(this.invalidHeading).toBeVisible()
    await expect(this.invalidReason).toBeVisible()
    await expect(this.page).toHaveURL(/\/confirm-email-change$/)
  }
}
