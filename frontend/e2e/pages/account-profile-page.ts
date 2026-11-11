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

  /**
   * Карточка аватара — первая на `/account/profile`, поэтому у секции есть
   * собственный `aria-labelledby`: иначе её не отличить от карточки личных данных.
   */
  get avatarSection(): Locator {
    return this.page.locator('section[aria-labelledby="account-avatar-heading"]')
  }

  /** Загруженный аватар карточки: реальный `<img>`, а не подложка-инициалы. */
  get avatarImage(): Locator {
    return this.avatarSection.locator('[data-slot="avatar-image"]')
  }

  get avatarFallback(): Locator {
    return this.avatarSection.locator('[data-slot="avatar-fallback"]')
  }

  /** Тот же аватар в каркасе кабинета (`AccountIdentity`). */
  get identityAvatarImage(): Locator {
    return this.identity.locator('[data-slot="avatar-image"]')
  }

  get avatarFileInput(): Locator {
    return this.avatarSection.locator('input[type="file"]')
  }

  get uploadAvatarButton(): Locator {
    return this.avatarSection.getByRole('button', {
      name: /^(Загрузить|Изменить) фото$/,
    })
  }

  get deleteAvatarButton(): Locator {
    return this.avatarSection.getByRole('button', {
      name: 'Удалить',
      exact: true,
    })
  }

  get avatarStatus(): Locator {
    return this.avatarSection.getByRole('status')
  }

  /**
   * Карточка «Email» — вторая на `/account/profile`, с собственным
   * `aria-labelledby`: адрес и статус подтверждения живут здесь, а не в форме.
   */
  get emailSection(): Locator {
    return this.page.locator('section[aria-labelledby="account-email-heading"]')
  }

  /** Статус подтверждения адреса внутри карточки. */
  get emailStatus(): Locator {
    return this.emailSection.getByText(/^Адрес (не )?подтверждён\.$/)
  }

  /** Кнопка карточки: после успеха она показывает локальный кулдаун. */
  get changeEmailButton(): Locator {
    return this.emailSection.getByRole('button', {
      name: /^(Изменить email|Повторить через)/,
    })
  }

  /** «Письмо отправлено на …» — `role="status"` карточки. */
  get emailChangeStatus(): Locator {
    return this.emailSection.getByRole('status')
  }

  get emailDialog(): Locator {
    return this.page.getByRole('dialog', { name: 'Изменить email' })
  }

  get newEmailInput(): Locator {
    return this.emailDialog.getByLabel('Новый email')
  }

  get currentPasswordInput(): Locator {
    return this.emailDialog.getByLabel('Текущий пароль')
  }

  get submitEmailChange(): Locator {
    return this.emailDialog.getByRole('button', {
      name: /^(Отправить письмо|Отправляем письмо…)$/,
    })
  }

  /** Ошибка уровня формы (`role="alert"`): кулдаун, недоступная почта, сеть. */
  get emailChangeFormError(): Locator {
    return this.emailDialog.getByRole('alert')
  }

  async openEmailChangeDialog(): Promise<void> {
    await this.changeEmailButton.click()
    await expect(this.emailDialog).toBeVisible()
  }

  /** Заполняет диалог и отправляет заявку, не дожидаясь ответа. */
  async requestEmailChange(
    newEmail: string,
    currentPassword: string
  ): Promise<void> {
    await this.newEmailInput.fill(newEmail)
    await this.currentPasswordInput.fill(currentPassword)
    await this.submitEmailChange.click()
  }

  get deleteAvatarDialog(): Locator {
    return this.page.getByRole('alertdialog', { name: 'Удалить аватар?' })
  }

  get deleteAvatarConfirm(): Locator {
    return this.deleteAvatarDialog.getByRole('button', {
      name: 'Удалить',
      exact: true,
    })
  }

  /** Выбор файла: кнопка открывает скрытый input, поэтому файл кладём прямо в него. */
  async uploadAvatar(filePath: string): Promise<void> {
    await this.avatarFileInput.setInputFiles(filePath)
  }

  async openAvatarDeleteDialog(): Promise<void> {
    await this.deleteAvatarButton.click()
    await expect(this.deleteAvatarDialog).toBeVisible()
  }

  async confirmAvatarDelete(): Promise<void> {
    await this.deleteAvatarConfirm.click()
  }

  /** Ждём профиль, а не скелетон: поле имени заполнено из API. */
  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.firstName).toHaveValue(/\S/)
  }
}
