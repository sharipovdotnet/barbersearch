import { expect, type Locator, type Page } from '@playwright/test'

/** Витрина `/`: форма поиска и сетка результатов. */
export class LandingPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { level: 1 })
  }

  get cityInput(): Locator {
    return this.page.getByLabel('Город салона')
  }

  get submit(): Locator {
    return this.page.getByRole('button', { name: 'Найти' })
  }

  get results(): Locator {
    return this.page.locator('section#barbers')
  }

  get cards(): Locator {
    return this.results.locator('ul a[href^="/barbers/"]')
  }

  /** «Показан 1 барбер» / «Показаны 5 барберов» — живой счётчик над сеткой. */
  get counter(): Locator {
    return this.results
      .locator('p:not([aria-live])')
      .filter({ hasText: /^Показан/ })
  }

  get showMore(): Locator {
    return this.page.getByRole('button', { name: 'Показать ещё' })
  }

  async search(city: string): Promise<void> {
    await this.cityInput.fill(city)
    await this.submit.click()
  }

  async cardTexts(): Promise<string[]> {
    return this.cards.allInnerTexts()
  }

  async expectCardCount(count: number): Promise<void> {
    await expect(this.cards).toHaveCount(count)
  }
}
