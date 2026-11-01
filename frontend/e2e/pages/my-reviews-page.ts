import { expect, type Locator, type Page } from '@playwright/test'

import { pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/** `/account/reviews` — «Мои отзывы»: список, правка и удаление. */
export class MyReviewsPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы отзывов')
  }

  async goto(): Promise<void> {
    await this.page.goto('/account/reviews')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Мои отзывы', level: 1 })
  }

  /** Заголовок списка: цель фокуса после смены страницы. */
  get listHeading(): Locator {
    return this.page.locator('h2#my-reviews-heading')
  }

  get rows(): Locator {
    return this.page.locator('section[aria-labelledby="my-reviews-heading"] ul > li')
  }

  get editDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  get deleteDialog(): Locator {
    return this.page.getByRole('alertdialog')
  }

  get statusMessage(): Locator {
    return this.page.locator('div[role="status"]')
  }

  async waitForFirstPage(): Promise<void> {
    await expect(this.rows).toHaveCount(pageSize.myReviews)
  }

  editButton(row: Locator): Locator {
    return row.getByRole('button', { name: /^Изменить отзыв о визите/ })
  }

  deleteButton(row: Locator): Locator {
    return row.getByRole('button', { name: /^Удалить отзыв о визите/ })
  }

  /** Оценка в диалоге правки: пять кнопок `N из 5` с `aria-pressed`. */
  ratingButton(rating: number): Locator {
    return this.editDialog.getByRole('button', { name: `${rating} из 5`, exact: true })
  }

  get commentField(): Locator {
    return this.editDialog.getByLabel(/Комментарий/)
  }

  get saveButton(): Locator {
    return this.editDialog.getByRole('button', { name: 'Сохранить' })
  }

  get confirmDelete(): Locator {
    return this.deleteDialog.getByRole('button', { name: 'Удалить отзыв' })
  }
}
