import { expect, type Locator, type Page } from '@playwright/test'

import { pageSize } from '../fixtures/accounts'
import { ListPagerComponent } from './list-pager-component'

/**
 * `/barber` — кабинет барбера.
 *
 * Три вкладки: лента дня (вид по умолчанию), «Ближайшие» и «История». Вкладки
 * живут на уровне страницы, поэтому объект страницы сначала переключает вид, а
 * потом ищет строки: панель одна на всех.
 */
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

  /**
   * Колокольчик уведомлений в заголовке кабинета (§8.1).
   *
   * Имя меняется вместе со счётчиком: «Уведомления, непрочитанных: 1»; при нуле
   * бейджа нет, а имя остаётся «Уведомления, непрочитанных: 0».
   */
  get notificationsBell(): Locator {
    return this.page.getByRole('link', { name: /^Уведомления/ })
  }

  get tablist(): Locator {
    return this.page.getByRole('tablist', { name: 'Какие записи показать' })
  }

  tab(name: 'Сегодня' | 'Ближайшие' | 'История'): Locator {
    return this.tablist.getByRole('tab', { name })
  }

  get panel(): Locator {
    return this.page.locator('#barber-bookings-panel')
  }

  /** Заголовок списка: цель фокуса после смены страницы и смены вкладки. */
  get listHeading(): Locator {
    return this.page.locator('h2#barber-bookings-heading')
  }

  /** Заголовок ленты дня. */
  get dayHeading(): Locator {
    return this.page.locator('h2#barber-day-heading')
  }

  /** Лента дня: строки записей вперемешку с окнами. */
  get dayList(): Locator {
    return this.page.getByRole('list', { name: 'Расписание на сегодня' })
  }

  get dayRows(): Locator {
    return this.dayList.locator('li')
  }

  /** Кнопка окна: доступное имя уточняет время, видимый текст — «Занять». */
  gapButton(): Locator {
    return this.panel.getByRole('button', { name: /^Занять время с / })
  }

  get rows(): Locator {
    return this.panel.locator('li')
  }

  /**
   * Кнопка правки цены в строке: ищется по доступному имени, а не по CSS-селектору.
   * Имя уточняет время визита, поэтому `^` достаточно: у каждой строки своя кнопка.
   */
  priceButtonOf(row: Locator): Locator {
    return row.getByRole('button', { name: /^Изменить цену визита/ })
  }

  /** Диалог правки цены: обычный `dialog`, а не подтверждение отмены. */
  get priceDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  get priceInput(): Locator {
    return this.priceDialog.getByLabel('Цена, ₽')
  }

  get savePriceButton(): Locator {
    return this.priceDialog.getByRole('button', { name: 'Сохранить' })
  }

  get actionDialog(): Locator {
    return this.page.getByRole('alertdialog')
  }

  /** Кнопка «Записать клиента» над списком ближайших записей. */
  get bookClientButton(): Locator {
    return this.page.getByRole('button', { name: 'Записать клиента' }).first()
  }

  /** Диалог ручной записи: клиент, услуга, салон, дата, слоты и комментарий. */
  get bookingDialog(): Locator {
    return this.page.getByRole('dialog')
  }

  async openBookingDialog(): Promise<void> {
    await this.bookClientButton.click()
    await expect(this.bookingDialog).toBeVisible()
  }

  async openTab(name: 'Сегодня' | 'Ближайшие' | 'История'): Promise<void> {
    await this.tab(name).click()
  }

  /**
   * Большинство сцен ждёт серверный список из двадцати строк: кабинет теперь
   * открывается на «Сегодня», поэтому сначала переключаем вид.
   */
  async waitForUpcoming(): Promise<void> {
    await this.openTab('Ближайшие')
    await expect(this.rows).toHaveCount(pageSize.cabinet)
  }
}
