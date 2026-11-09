import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/forgot-password` — запрос ссылки для сброса забытого пароля.
 *
 * Два состояния: `form` и `sent`. Ответ сервера одинаков для существующего и
 * несуществующего адреса (анти-энумерация), поэтому экран успеха один и тот же —
 * generic-карточка «Проверьте почту» с адресом в тексте, а не отдельные тексты
 * «аккаунт есть»/«аккаунта нет».
 */
export class ForgotPasswordPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/forgot-password')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Восстановление пароля',
      level: 1,
    })
  }

  get email(): Locator {
    return this.page.getByLabel('Email', { exact: true })
  }

  get submit(): Locator {
    return this.page.getByRole('button', { name: 'Отправить ссылку' })
  }

  /** Ошибка сервера/сети на форме (`role="alert"`). */
  get formError(): Locator {
    return this.page.getByRole('alert')
  }

  get sentHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Проверьте почту', level: 1 })
  }

  /** Generic-текст анти-энумерации: «Если аккаунт с адресом … существует…». */
  get sentDescription(): Locator {
    return this.page.getByText(/Если аккаунт с адресом/)
  }

  /**
   * Повторная отправка: до кулдауна «Отправить ещё раз», после — «Повторить через
   * N с» и disabled. Кулдаун локальный, сервер о нём молчит (§3.4).
   */
  get resendButton(): Locator {
    return this.page.getByRole('button', {
      name: /^(Отправить ещё раз|Повторить через \d+ с)$/,
    })
  }

  get anotherAddress(): Locator {
    return this.page.getByRole('button', { name: 'Указать другой адрес' })
  }

  get homeLink(): Locator {
    return this.page.getByRole('link', { name: 'На главную' })
  }

  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.email).toBeVisible()
    await expect(this.submit).toBeVisible()
  }

  /** Один запрос ссылки: заполнить email и отправить. */
  async requestLink(email: string): Promise<void> {
    await this.email.fill(email)
    await this.submit.click()
  }

  /** Успех: карточка «Проверьте почту» и generic-текст с адресом. */
  async waitForSent(email: string): Promise<void> {
    await expect(this.sentHeading).toBeVisible()
    await expect(this.sentDescription).toContainText(email)
    await expect(this.page).toHaveURL(/\/forgot-password$/)
  }
}
