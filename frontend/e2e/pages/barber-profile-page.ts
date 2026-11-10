import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/barber/profile` — свой профиль барбера: карточка, прайс и специализации.
 *
 * Один экран на один агрегат: отдельного маршрута для услуг нет. Поэтому здесь
 * три раздела и три диалога (добавление, цена, скрытие), а действия строки
 * называются так же, как их видит барбер: «Изменить цену», «Скрыть», «Вернуть».
 *
 * Скрытие — `alertdialog` с тем же словом, что и кнопка строки, поэтому
 * подтверждение всегда ищется по роли `alertdialog`, а не по тексту.
 */
export class BarberProfilePage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/barber/profile')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Профиль', level: 1 })
  }

  get nav(): Locator {
    return this.page.getByRole('navigation', { name: 'Разделы кабинета барбера' })
  }

  navLink(name: string): Locator {
    return this.nav.getByRole('link', { name, exact: true })
  }

  get displayName(): Locator {
    return this.page.getByLabel('Как вас представить')
  }

  get experience(): Locator {
    return this.page.getByLabel('Опыт, лет')
  }

  get bio(): Locator {
    return this.page.getByLabel('О себе')
  }

  get latePolicy(): Locator {
    return this.page.getByLabel('Опоздания')
  }

  get saveProfile(): Locator {
    return this.page.getByRole('button', { name: 'Сохранить', exact: true })
  }

  /** Объявление успеха: `role="status"` с точным текстом операции. */
  announcement(message: string): Locator {
    return this.page.getByRole('status').filter({ hasText: message })
  }

  get publicLink(): Locator {
    return this.page.getByRole('link', { name: 'Открыть, как видят клиенты' })
  }

  get unverifiedNotice(): Locator {
    return this.page.getByText('Профиль на проверке.', { exact: false })
  }

  /** Секция «Фотографии» между «Карточкой» и «Услугами» (§5.3). */
  get photosSection(): Locator {
    return this.page.locator(
      'section[aria-labelledby="barber-profile-photos-heading"]'
    )
  }

  get emptyPhotos(): Locator {
    return this.photosSection.getByText('Фотографий нет.', { exact: false })
  }

  get addPhotoButton(): Locator {
    return this.photosSection.getByRole('button', {
      name: 'Добавить фото',
      exact: true,
    })
  }

  get uploadDialog(): Locator {
    return this.page.getByRole('dialog', { name: 'Добавить фото' })
  }

  get photoFileInput(): Locator {
    return this.uploadDialog.getByLabel('Файл', { exact: true })
  }

  get photoCaption(): Locator {
    return this.uploadDialog.getByLabel('Подпись', { exact: true })
  }

  get uploadSubmit(): Locator {
    return this.uploadDialog.getByRole('button', {
      name: 'Загрузить',
      exact: true,
    })
  }

  get deleteDialog(): Locator {
    return this.page.getByRole('alertdialog', { name: 'Удалить фото?' })
  }

  get deleteConfirm(): Locator {
    return this.deleteDialog.getByRole('button', {
      name: 'Удалить',
      exact: true,
    })
  }

  /** Карточки фотографий в порядке `SortOrder`: сетка секции «Фотографии». */
  photoCards(): Locator {
    return this.photosSection
      .getByRole('list', { name: 'Фотографии', exact: true })
      .getByRole('listitem')
  }

  /**
   * Показать секцию «Фотографии»: на `/barber/profile` это якорь-секция, а не
   * отдельный маршрут, поэтому «открыть» значит дождаться её и прокрутить к ней.
   */
  async openPortfolio(): Promise<void> {
    await expect(this.photosSection).toBeVisible()
    await this.photosSection.scrollIntoViewIfNeeded()
  }

  /** Загрузка: файл, подпись и «Загрузить»; успех объявляет вызывающий тест. */
  async uploadPhoto(filePath: string, description: string): Promise<void> {
    await this.addPhotoButton.click()
    await expect(this.uploadDialog).toBeVisible()

    await this.photoFileInput.setInputFiles(filePath)

    if (description !== '') {
      await this.photoCaption.fill(description)
    }

    await this.uploadSubmit.click()
  }

  /**
   * Нажать «Удалить» у фотографии и дождаться `alertdialog`: подтверждение
   * остаётся за тестом, чтобы он проверил текст последствия.
   */
  async deletePhoto(index: number): Promise<void> {
    await this.photoCards()
      .nth(index)
      .getByRole('button', { name: /^Удалить:/ })
      .click()
    await expect(this.deleteDialog).toBeVisible()
  }

  async confirmDelete(): Promise<void> {
    await this.deleteConfirm.click()
  }

  get servicesSection(): Locator {
    return this.page.locator(
      'section[aria-labelledby="barber-profile-services-heading"]'
    )
  }

  get servicesList(): Locator {
    return this.page.getByRole('list', { name: 'Услуги', exact: true })
  }

  serviceRow(name: string): Locator {
    return this.servicesList.getByRole('listitem').filter({ hasText: name })
  }

  get emptyServices(): Locator {
    return this.servicesSection.getByText('Услуг нет.', { exact: false })
  }

  get addServiceButton(): Locator {
    return this.servicesSection.getByRole('button', {
      name: 'Добавить услугу',
      exact: true,
    })
  }

  get addDialog(): Locator {
    return this.page.getByRole('dialog', { name: 'Добавить услугу' })
  }

  get serviceSelect(): Locator {
    return this.addDialog.getByLabel('Услуга', { exact: true })
  }

  get addPrice(): Locator {
    return this.addDialog.getByLabel('Цена, ₽')
  }

  get addDuration(): Locator {
    return this.addDialog.getByLabel('Длительность, мин')
  }

  get addSubmit(): Locator {
    return this.addDialog.getByRole('button', { name: 'Добавить', exact: true })
  }

  get priceDialog(): Locator {
    return this.page.getByRole('dialog', { name: 'Изменить цену' })
  }

  get priceInput(): Locator {
    return this.priceDialog.getByLabel('Цена, ₽')
  }

  get priceDuration(): Locator {
    return this.priceDialog.getByLabel('Длительность, мин')
  }

  get priceSubmit(): Locator {
    return this.priceDialog.getByRole('button', { name: 'Сохранить' })
  }

  get hideDialog(): Locator {
    return this.page.getByRole('alertdialog', { name: 'Скрыть услугу?' })
  }

  get hideConfirm(): Locator {
    return this.hideDialog.getByRole('button', { name: 'Скрыть', exact: true })
  }

  get specializationsSection(): Locator {
    return this.page.locator(
      'section[aria-labelledby="barber-profile-specializations-heading"]'
    )
  }

  get emptySpecializations(): Locator {
    return this.specializationsSection.getByText('Специализаций нет.', {
      exact: false,
    })
  }

  get specializationSelect(): Locator {
    return this.specializationsSection.getByLabel('Специализация', {
      exact: true,
    })
  }

  get addSpecializationButton(): Locator {
    return this.specializationsSection.getByRole('button', {
      name: 'Добавить',
      exact: true,
    })
  }

  /**
   * Ждём карточку из API, а не скелетон: поле имени заполнено, разделы на месте.
   * Скелетон тоже `role="status"`, поэтому одного `role="status"` недостаточно.
   */
  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.displayName).toHaveValue(/\S/)
    await expect(this.servicesSection).toBeVisible()
    await expect(this.specializationsSection).toBeVisible()
  }
}
