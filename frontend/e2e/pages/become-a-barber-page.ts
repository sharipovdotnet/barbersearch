import { expect, type Locator, type Page } from '@playwright/test'

import { fixtures } from '../fixtures/accounts'

/**
 * `/become-a-barber` — заявка клиента на роль барбера.
 *
 * Страница одна на три состояния: анонимный текст, форма клиента и карточка со
 * статусом. Локаторы идут от ролей и подписей: `data-*`-хуков в разметке нет.
 * После `201` форма заменяется карточкой «Заявка отправлена», поэтому `sentHeading`
 * и `status` проверяются вместе — второй запрос `GET /mine` для этого не нужен.
 */
export class BecomeABarberPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/become-a-barber')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', {
      name: 'Стать барбером',
      level: 1,
    })
  }

  get benefitsHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Что даёт статус барбера' })
  }

  get formHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Заявка на роль барбера' })
  }

  get displayName(): Locator {
    return this.page.getByLabel('Как вас представить')
  }

  get yearsOfExperience(): Locator {
    return this.page.getByLabel('Опыт, лет')
  }

  get city(): Locator {
    return this.page.getByLabel('Город')
  }

  get about(): Locator {
    return this.page.getByLabel('О себе — необязательно')
  }

  get hasSalon(): Locator {
    return this.page.getByRole('checkbox', { name: 'Уже работаю в салоне' })
  }

  get submit(): Locator {
    return this.page.getByRole('button', { name: 'Отправить заявку' })
  }

  get sentHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Заявка отправлена' })
  }

  get pendingHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Заявка на рассмотрении' })
  }

  get rejectedHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Заявка отклонена' })
  }

  /** Статус как `role="status"`: «На рассмотрении», «Одобрена», «Отклонена». */
  get status(): Locator {
    return this.page.getByRole('status')
  }

  /** Причина отказа: текст из карточки статуса. */
  rejectionReason(text: string): Locator {
    return this.page.getByText(text)
  }

  /** Заполняет форму значениями фикстур и отправляет её. */
  async submitRequest(
    values: {
      displayName?: string
      yearsOfExperience?: number
      city?: string
      about?: string
    } = {}
  ): Promise<void> {
    const request = fixtures.barberRequest

    await this.displayName.fill(values.displayName ?? request.displayName)
    await this.yearsOfExperience.fill(
      String(values.yearsOfExperience ?? request.yearsOfExperience)
    )
    await this.city.fill(values.city ?? request.city)

    if (values.about) {
      await this.about.fill(values.about)
    }

    await this.submit.click()

    await expect(this.sentHeading).toBeVisible()
    await expect(this.status).toContainText('На рассмотрении')
  }
}
