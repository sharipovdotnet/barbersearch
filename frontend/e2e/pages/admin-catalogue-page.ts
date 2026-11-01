import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/admin/catalogue` — три справочника в одном экране.
 *
 * На `lg` и шире строки — это `<tr>`, уже — карточки `<li>`: локаторы берут видимую ветку,
 * поэтому один и тот же спека работает в desktop и mobile проектах. Пейджера здесь нет
 * по контракту, и `expectNoPager` это доказывает, а не проверяет «нет данных».
 */

export type CatalogueTabName = 'services' | 'specializations' | 'salons'

const TAB_LABELS: Record<CatalogueTabName, string> = {
  services: 'Услуги',
  specializations: 'Специализации',
  salons: 'Салоны',
}

export type CatalogueStatusLabel = 'Все' | 'Активные' | 'Выключенные'

export class AdminCataloguePage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(tab: CatalogueTabName = 'services'): Promise<void> {
    await this.page.goto(
      tab === 'services' ? '/admin/catalogue' : `/admin/catalogue?tab=${tab}`
    )
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Каталог', level: 1 })
  }

  get tabs(): Locator {
    return this.page.getByRole('tablist', { name: 'Какой справочник показать' })
  }

  get activeTab(): Locator {
    return this.tabs.getByRole('tab', { selected: true })
  }

  tab(name: CatalogueTabName): Locator {
    return this.tabs.getByRole('tab', { name: TAB_LABELS[name] })
  }

  async selectTab(name: CatalogueTabName): Promise<void> {
    await this.tab(name).click()
  }

  get panel(): Locator {
    return this.page.getByRole('tabpanel')
  }

  get table(): Locator {
    return this.panel.locator('table')
  }

  get caption(): Locator {
    return this.table.locator('caption')
  }

  /** Видимые строки: таблица на широком экране либо карточки на узком. */
  get rows(): Locator {
    return this.panel
      .locator('tbody tr, ul > li')
      .filter({ visible: true })
  }

  row(name: string): Locator {
    return this.rows.filter({ hasText: name })
  }

  get statusFilter(): Locator {
    return this.page.getByRole('group', { name: 'Статус строк каталога' })
  }

  status(name: CatalogueStatusLabel): Locator {
    return this.statusFilter.getByRole('button', { name, exact: true })
  }

  async selectStatus(name: CatalogueStatusLabel): Promise<void> {
    await this.status(name).click()
  }

  /** Подпись поля поиска отличается у салонов: «…по названию и городу». */
  get search(): Locator {
    return this.page.getByLabel(/^Поиск по названию/)
  }

  async searchFor(value: string): Promise<void> {
    await this.search.fill(value)
  }

  get refreshButton(): Locator {
    return this.page.getByRole('button', { name: 'Обновить', exact: true })
  }

  get createButton(): Locator {
    return this.page.getByRole('button', { name: /^Создать / })
  }

  get dialog(): Locator {
    return this.page.getByRole('dialog')
  }

  /** Подтверждение выключения: `role="alertdialog"`. */
  get actionDialog(): Locator {
    return this.page.getByRole('alertdialog')
  }

  editButton(rowName: string): Locator {
    return this.row(rowName).getByRole('button', { name: 'Изменить' })
  }

  toggleButton(rowName: string): Locator {
    return this.row(rowName).getByRole('button', {
      name: /^(Выключить|Включить)$/,
    })
  }

  async waitForRows(): Promise<void> {
    await expect(this.rows.first()).toBeVisible()
  }

  /** Пейджера и «Показать ещё» у справочника быть не может. */
  async expectNoPager(): Promise<void> {
    await expect(this.page.getByRole('button', { name: 'Показать ещё' })).toHaveCount(0)
    await expect(this.page.locator('nav[aria-label^="Страницы"]')).toHaveCount(0)
  }
}
