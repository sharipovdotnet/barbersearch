import { expect, type Locator, type Page } from '@playwright/test'

import { pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/**
 * `/account` — кабинет клиента: hero «Ближайшая запись» и две серверные вкладки.
 *
 * У каждой вкладки свой скоуп и свой курсор, поэтому пагинаторов два: один на
 * «Предстоящие», другой на «Историю». Разметка строк — обычный `<li>` внутри
 * `#bookings-panel`, как у остальных списков.
 */
export class AccountBookingsPage {
  readonly page: Page
  readonly upcomingPager: ListPagerComponent
  readonly historyPager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.upcomingPager = new ListPagerComponent(
      page,
      'Страницы предстоящих записей'
    )
    this.historyPager = new ListPagerComponent(page, 'Страницы истории')
  }

  async goto(): Promise<void> {
    await this.page.goto('/account')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Мои записи', level: 1 })
  }

  get tablist(): Locator {
    return this.page.getByRole('tablist', { name: 'Какие записи показать' })
  }

  tab(name: 'Предстоящие' | 'История'): Locator {
    return this.tablist.getByRole('tab', { name })
  }

  get panel(): Locator {
    return this.page.locator('#bookings-panel')
  }

  get rows(): Locator {
    return this.panel.locator('li')
  }

  /** Карточка «Ближайшая запись»: hero собирается из первой строки «Предстоящих». */
  get hero(): Locator {
    return this.page
      .locator('[data-slot="card"]')
      .filter({ hasText: 'Ближайшая запись' })
  }

  get cancelDialog(): Locator {
    return this.page.getByRole('alertdialog')
  }

  async openTab(name: 'Предстоящие' | 'История'): Promise<void> {
    await this.tab(name).click()
  }

  /** Первая страница «Предстоящих» пришла с сервера. */
  async waitForUpcoming(): Promise<void> {
    await expect(this.rows).toHaveCount(pageSize.account)
  }
}
