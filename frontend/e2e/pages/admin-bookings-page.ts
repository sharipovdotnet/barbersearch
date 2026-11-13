import { expect, type Locator, type Page, type Response } from '@playwright/test'

import { fixtures } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/** Фильтр статуса: те же значения, что в `<select>` экрана (`all` — без фильтра). */
export type AdminBookingsStatus =
  | 'all'
  | 'Pending'
  | 'Confirmed'
  | 'Completed'
  | 'Cancelled'
  | 'NoShow'

/**
 * `/admin/bookings` — рабочий стол поддержки: список и карточка брони.
 *
 * Строки бывают двух видов: `<tr>` таблицы на `lg` и карточки `<li>` на узком
 * экране. Локатор `rows` берёт видимую ветку, поэтому один спек работает и в
 * `desktop-chromium`, и в `mobile-chromium`. Строка опознаётся по `href` ссылки:
 * id брони уже стоит в адресе карточки, поэтому `data-*`-хук в разметке не нужен.
 * Когда на телефоне открыта карточка, список скрыт — `rows` остаётся пустым.
 *
 * Фильтры (поиск, статус) живут в состоянии React и в URL не попадают: в адресе
 * остаётся только id брони. Поэтому каждый помощник ждёт настоящий ответ списка
 * с ожидаемыми параметрами, а не таймаут, — поиск на экране дебаунсится 300 мс.
 */
export class AdminBookingsPage {
  readonly page: Page
  readonly pager: ListPagerComponent

  constructor(page: Page) {
    this.page = page
    this.pager = new ListPagerComponent(page, 'Страницы броней')
  }

  async goto(bookingId: string | null = null): Promise<void> {
    await this.page.goto(
      bookingId ? `/admin/bookings/${bookingId}` : '/admin/bookings'
    )
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Брони', level: 1 })
  }

  get subtitle(): Locator {
    return this.page.getByText(
      'Все записи клиентов. Время показано в поясе салона.'
    )
  }

  get searchField(): Locator {
    return this.page.getByRole('searchbox', { name: 'Клиент или номер брони' })
  }

  get statusSelect(): Locator {
    return this.page.getByRole('combobox', { name: 'Статус' })
  }

  get resetFiltersButton(): Locator {
    return this.page.getByRole('button', { name: 'Сбросить фильтры' })
  }

  get refreshButton(): Locator {
    return this.page.getByRole('button', { name: 'Обновить', exact: true })
  }

  /** Видимые строки: `<tr>` таблицы на `lg` либо `<li>` карточек на узком экране. */
  get rows(): Locator {
    return this.page
      .locator('tbody tr, ul[aria-label="Список броней"] > li')
      .filter({ visible: true })
  }

  /** Ссылка строки: ведёт на карточку, поэтому id — стабильный ключ строки. */
  rowLink(bookingId: string): Locator {
    return this.rows.locator(`a[href="/admin/bookings/${bookingId}"]`)
  }

  /** Строка по id брони: ссылка на карточку есть и в таблице, и в карточках. */
  rowOf(bookingId: string): Locator {
    return this.rows.filter({
      has: this.page.locator(`a[href="/admin/bookings/${bookingId}"]`),
    })
  }

  /** Выбранная строка: `aria-current="page"` есть только у ссылки в списке. */
  get selectedLink(): Locator {
    return this.page.locator('a[href^="/admin/bookings/"][aria-current="page"]')
  }

  get detail(): Locator {
    return this.page.locator('#booking-detail')
  }

  get detailHeading(): Locator {
    return this.detail.getByRole('heading', { name: 'Бронь', level: 2 })
  }

  /**
   * Бейдж статуса в карточке.
   *
   * Точное совпадение текста важно: `toContainText('Отменена')` поймал бы и
   * объявление «Бронь отменена.» — регистр при подстроке не учитывается.
   */
  detailStatus(label: string): Locator {
    return this.detail.getByText(label, { exact: true })
  }

  /** Кнопки действий карточки. Диалог подтверждения порталится, поэтому не мешает. */
  actionButton(
    name: 'Отменить бронь' | 'Отметить неявку' | 'Изменить цену визита'
  ): Locator {
    return this.detail.getByRole('button', { name })
  }

  get confirmDialog(): Locator {
    return this.page.getByRole('alertdialog')
  }

  /** Диалог правки цены визита: обычный `dialog`, а не подтверждение. */
  get priceDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  /** Поле «Цена, ₽» внутри диалога цены. */
  get priceInput(): Locator {
    return this.priceDialog.getByLabel('Цена, ₽')
  }

  /** Кнопка сохранения в диалоге цены; недоступна при неизменной сумме. */
  get savePriceButton(): Locator {
    return this.priceDialog.getByRole('button', { name: 'Сохранить' })
  }

  async searchFor(
    value: string,
    status: AdminBookingsStatus = 'all'
  ): Promise<void> {
    const response = this.firstPageResponse(status, value.trim())

    await this.searchField.fill(value)
    await response
  }

  async selectStatus(
    status: AdminBookingsStatus,
    search = ''
  ): Promise<void> {
    const response = this.firstPageResponse(status, search)

    await this.statusSelect.selectOption(status)
    await response
  }

  async resetFilters(): Promise<void> {
    const response = this.firstPageResponse('all')

    await this.resetFiltersButton.click()
    await response
  }

  async waitForRows(
    count: number = fixtures.adminBookings.pageSize
  ): Promise<void> {
    await expect(this.rows).toHaveCount(count)
  }

  /**
   * Открывает карточку по ссылке строки и ждёт именно это чтение.
   *
   * Адрес меняется мгновенно (`pushState`), а карточка приходит сетью: без
   * ожидания ответа следующий шаг мог бы прочитать скелетон.
   */
  async openBooking(bookingId: string): Promise<void> {
    const response = this.page.waitForResponse((candidate) => {
      const url = new URL(candidate.url())

      return (
        url.pathname === `/api/admin/bookings/${bookingId}` &&
        candidate.status() === 200
      )
    })

    await this.rowLink(bookingId).click()
    await response

    await expect(this.detailHeading).toBeVisible()
  }

  /** «Показать ещё» у броней быть не может: список листается общим пейджером. */
  async expectNoLoadMore(): Promise<void> {
    await expect(
      this.page.getByRole('button', { name: 'Показать ещё', exact: true })
    ).toHaveCount(0)
  }

  /**
   * Брони — только чтение плюс три операции в карточке.
   *
   * У строк нет кнопок действий, на экране нет создания, переноса, правки и
   * удаления, а общего числа записей сервер не отдаёт — счётчика «всего» нет.
   */
  async expectReadOnly(): Promise<void> {
    await this.expectNoLoadMore()

    for (const name of [
      'Создать бронь',
      'Перенести',
      'Редактировать',
      'Удалить',
    ]) {
      await expect(
        this.page.getByRole('button', { name, exact: true })
      ).toHaveCount(0)
    }

    await expect(this.page.getByText(/всего/i)).toHaveCount(0)
    await expect(this.rows.getByRole('button')).toHaveCount(0)
  }

  /**
   * Первая страница списка с точными фильтрами.
   *
   * `cursor` в ожидании отсутствует: смена поиска или статуса начинает список
   * заново. `status` уходит всегда, `search` — только непустым, как в контракте.
   */
  private firstPageResponse(
    status: AdminBookingsStatus,
    search = ''
  ): Promise<Response> {
    return this.page.waitForResponse((response) => {
      if (response.status() !== 200) {
        return false
      }

      const url = new URL(response.url())

      return (
        url.pathname === '/api/admin/bookings' &&
        url.searchParams.get('status') === status &&
        (url.searchParams.get('search') ?? '') === search &&
        !url.searchParams.has('cursor')
      )
    })
  }
}
