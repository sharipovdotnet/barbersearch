import { expect, type Locator, type Page } from '@playwright/test'

import { pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/** `/barber` — кабинет барбера: вкладки «Ближайшие»/«История» и действия строк. */
export class CabinetPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы записей')
  }

  async goto(): Promise<void> {
    await this.page.goto('/barber')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Кабинет барбера', level: 1 })
  }

  get tablist(): Locator {
    return this.page.getByRole('tablist', { name: 'Какие записи показать' })
  }

  tab(name: 'Ближайшие' | 'История'): Locator {
    return this.tablist.getByRole('tab', { name })
  }

  /** Заголовок списка: цель фокуса после смены страницы и смены вкладки. */
  get listHeading(): Locator {
    return this.page.locator('h2#barber-bookings-heading')
  }

  get panel(): Locator {
    return this.page.locator('#barber-bookings-panel')
  }

  get rows(): Locator {
    return this.panel.locator('li')
  }

  get actionDialog(): Locator {
    return this.page.getByRole('alertdialog')
  }

  async openTab(name: 'Ближайшие' | 'История'): Promise<void> {
    await this.tab(name).click()
  }

  async waitForUpcoming(): Promise<void> {
    await expect(this.rows).toHaveCount(pageSize.cabinet)
  }
}
