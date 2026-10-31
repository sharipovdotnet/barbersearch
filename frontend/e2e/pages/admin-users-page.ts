import { expect, type Locator, type Page } from '@playwright/test'

import { pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/** `/admin/users` — список пользователей: фильтры, поиск, карточка. */
export class AdminUsersPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы пользователей')
  }

  async goto(): Promise<void> {
    await this.page.goto('/admin/users')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Пользователи', level: 1 })
  }

  get list(): Locator {
    return this.page.locator('section[aria-label="Пользователи"]')
  }

  get rows(): Locator {
    return this.list.locator('li > button')
  }

  get selectedRow(): Locator {
    return this.list.locator('button[aria-current="true"]')
  }

  get statusFilter(): Locator {
    return this.page.getByRole('group', { name: 'Статус пользователей' })
  }

  get roleFilter(): Locator {
    return this.page.getByRole('group', { name: 'Роль пользователей' })
  }

  get search(): Locator {
    return this.page.getByLabel('Поиск по имени или email')
  }

  get refresh(): Locator {
    return this.page.getByRole('button', { name: 'Обновить' })
  }

  async waitForFirstPage(): Promise<void> {
    await expect(this.rows).toHaveCount(pageSize.admin)
  }

  async selectStatus(name: string): Promise<void> {
    await this.statusFilter.getByRole('button', { name }).click()
  }

  async selectRole(name: string): Promise<void> {
    await this.roleFilter.getByRole('button', { name }).click()
  }
}
