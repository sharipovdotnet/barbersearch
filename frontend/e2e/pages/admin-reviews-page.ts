import { expect, type Locator, type Page } from '@playwright/test'

import { pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/** `/admin/reviews` — очередь модерации: вкладки, выбор отзыва, обновление. */
export class AdminReviewsPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы отзывов')
  }

  async goto(tab?: 'published'): Promise<void> {
    await this.page.goto(tab === 'published' ? '/admin/reviews?tab=published' : '/admin/reviews')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Отзывы', level: 1 })
  }

  get tablist(): Locator {
    return this.page.getByRole('tablist', { name: 'Какие отзывы показать' })
  }

  tab(name: 'На модерации' | 'Опубликованные'): Locator {
    return this.tablist.getByRole('tab', { name })
  }

  /** Очередь: `section#reviews-list`, строки — кнопки с составным `aria-label`. */
  get list(): Locator {
    return this.page.locator('section#reviews-list')
  }

  get rows(): Locator {
    return this.list.locator('li > button')
  }

  get selectedRow(): Locator {
    return this.list.locator('button[aria-current="true"]')
  }

  get refresh(): Locator {
    return this.page.getByRole('button', { name: 'Обновить' })
  }

  async openTab(name: 'На модерации' | 'Опубликованные'): Promise<void> {
    await this.tab(name).click()
  }

  async waitForFirstPage(): Promise<void> {
    await expect(this.rows).toHaveCount(pageSize.admin)
  }

  /**
   * id отзыва из доступного имени строки: `aria-labelledby` ссылается на
   * `review-<id>-rating`, `-name` и `-meta`.
   */
  async reviewId(row: Locator): Promise<string> {
    const labelledBy = await row.getAttribute('aria-labelledby')
    const match = labelledBy?.match(/^review-([0-9a-f-]{36})-rating/)

    expect(
      match,
      `строка ссылается на видимые спаны, получено «${labelledBy}»`
    ).not.toBeNull()

    return (match as RegExpExecArray)[1]
  }
}
