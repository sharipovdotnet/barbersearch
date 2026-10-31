import { expect, type Locator, type Page } from '@playwright/test'

import { fixtures, pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/** Публичный профиль `/barbers/{id}` — секция «Отзывы». */
export class ProfilePage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы отзывов')
  }

  async goto(barberProfileId: string = fixtures.barberProfileId): Promise<void> {
    await this.page.goto(`/barbers/${barberProfileId}`)
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Отзывы', level: 2 })
  }

  /** Список отзывов: он же цель фокуса после смены страницы. */
  get reviewsList(): Locator {
    return this.page.locator('ul[aria-label="Отзывы барбера"]')
  }

  get reviewCards(): Locator {
    return this.reviewsList.locator('li')
  }

  get summary(): Locator {
    return this.page
      .locator('section[aria-labelledby="barber-reviews-heading"]')
      .getByText(/\d+ (отзыв|отзыва|отзывов)/)
      .first()
  }

  /** Ждём именно первую страницу, а не скелетон: 10 строк и пагинатор. */
  async waitForFirstPage(): Promise<void> {
    await expect(this.reviewCards).toHaveCount(pageSize.reviews)
  }

  async cardTexts(): Promise<string[]> {
    return this.reviewCards.allInnerTexts()
  }
}
