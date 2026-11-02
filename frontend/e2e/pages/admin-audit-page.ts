import { expect, type Locator, type Page } from '@playwright/test'

import { fixtures } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/**
 * `/admin/audit` — read-only журнал изменений.
 *
 * Строка опознаётся по `data-entry-id` (id записи аудита из `seed.sql`): других
 * стабильных признаков у неё нет — время показывается в поясе браузера, а имя
 * сущности сервер намеренно не резолвит. Фильтры живут в состоянии, поэтому
 * проверок «фильтр в URL» здесь нет: наоборот, URL не должен меняться.
 */
export class AdminAuditPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы журнала')
  }

  async goto(): Promise<void> {
    await this.page.goto('/admin/audit')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Журнал', level: 1 })
  }

  get subtitle(): Locator {
    return this.page.getByText(
      'История изменений, которую пишет сервер. Записи не редактируются.'
    )
  }

  get list(): Locator {
    return this.page.getByRole('list', { name: 'Записи журнала' })
  }

  get rows(): Locator {
    return this.list.getByRole('listitem')
  }

  /** Строка по id записи аудита: `9e700000-…` из фикстур. */
  row(entryId: string): Locator {
    return this.list.locator(`li[data-entry-id="${entryId}"]`)
  }

  /** Единственная кнопка строки — раскрытие diff'а; имя меняется на «Скрыть…». */
  toggle(entryId: string): Locator {
    return this.row(entryId).getByRole('button')
  }

  expandButton(entryId: string): Locator {
    return this.row(entryId).getByRole('button', { name: 'Показать изменения' })
  }

  collapseButton(entryId: string): Locator {
    return this.row(entryId).getByRole('button', { name: 'Скрыть изменения' })
  }

  diff(entryId: string): Locator {
    return this.row(entryId).locator('dl')
  }

  get entityTypeSelect(): Locator {
    return this.page.getByRole('combobox', { name: 'Тип сущности' })
  }

  get actorInput(): Locator {
    return this.page.getByLabel('Авторская электронная почта')
  }

  get fromInput(): Locator {
    return this.page.getByLabel('С даты')
  }

  get toInput(): Locator {
    return this.page.getByLabel('По дату')
  }

  get resetFiltersButton(): Locator {
    return this.page.getByRole('button', { name: 'Сбросить фильтры' })
  }

  get refreshButton(): Locator {
    return this.page.getByRole('button', { name: 'Обновить', exact: true })
  }

  /**
   * Заполняет обе даты и ждёт ответ именно на итоговый запрос.
   *
   * Два поля — два изменения состояния; без ожидания клик «Дальше» мог бы попасть
   * в момент, когда вторая дата ещё не ушла в запрос. Ждём реальный HTTP-ответ,
   * а не таймаут.
   */
  async filterByDate(day: string): Promise<void> {
    await Promise.all([
      this.page.waitForResponse((response) => {
        const url = new URL(response.url())

        return (
          url.pathname === '/api/admin/audit' &&
          url.searchParams.get('from') === day &&
          url.searchParams.get('to') === day &&
          response.status() === 200
        )
      }),
      (async () => {
        await this.fromInput.fill(day)
        await this.toInput.fill(day)
      })(),
    ])
  }

  async selectEntityType(label: string): Promise<void> {
    await this.entityTypeSelect.selectOption({ label })
  }

  async waitForFirstPage(): Promise<void> {
    await expect(this.rows).toHaveCount(fixtures.journal.pageSize)
  }

  /** «Показать ещё» у журнала быть не может: он листается общим пейджером. */
  async expectNoLoadMore(): Promise<void> {
    await expect(
      this.page.getByRole('button', { name: 'Показать ещё', exact: true })
    ).toHaveCount(0)
  }

  /**
   * Журнал неизменяем: у строки нет действий, кроме раскрытия, а на экране нет
   * «Показать ещё», «Изменить», «Выключить» и счётчика «всего записей».
   */
  async expectReadOnly(): Promise<void> {
    await this.expectNoLoadMore()

    await expect(
      this.page.getByRole('button', { name: 'Изменить', exact: true })
    ).toHaveCount(0)
    await expect(
      this.page.getByRole('button', { name: 'Выключить', exact: true })
    ).toHaveCount(0)
    await expect(this.page.getByText(/всего записей/i)).toHaveCount(0)

    const rowCount = await this.rows.count()
    const rowButtons = this.rows.getByRole('button')

    await expect(rowButtons).toHaveCount(rowCount)

    const names = await rowButtons.evaluateAll((buttons) =>
      buttons.map((button) => (button.textContent ?? '').trim())
    )

    expect(
      names.every((name) => /^(Показать|Скрыть) изменения$/.test(name)),
      `кнопки строк — только раскрытие diff'а, получено: ${names.join(', ')}`
    ).toBe(true)
  }
}
