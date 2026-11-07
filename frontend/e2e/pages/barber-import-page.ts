import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/barber/import` — перенос старых записей из вставленного списка.
 *
 * Экран живёт тремя шагами на одном адресе: форма, проверка и отчёт. Объект
 * страницы называет каждый шаг своим локатором, чтобы спек читался как путь
 * барбера, а не как набор CSS-селекторов.
 */
export class BarberImportPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/barber/import')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Перенести записи' })
  }

  get cabinetHeading(): Locator {
    return this.page.getByRole('heading', { name: 'Кабинет барбера', level: 1 })
  }

  get backLink(): Locator {
    return this.page.getByRole('link', { name: '← Записи клиентов' })
  }

  get textarea(): Locator {
    return this.page.getByLabel('Список записей')
  }

  get parseButton(): Locator {
    return this.page.getByRole('button', { name: 'Разобрать' })
  }

  get clearButton(): Locator {
    return this.page.getByRole('button', { name: 'Очистить' })
  }

  get exampleToggle(): Locator {
    return this.page.getByRole('button', { name: /Показать пример|Скрыть пример/ })
  }

  /** Ссылка на политику внутри формы: в подвале такая же, поэтому скоуп — форма. */
  get privacyLink(): Locator {
    return this.page
      .locator('form')
      .getByRole('link', { name: 'Политика обработки данных' })
  }

  /** Сводка предпросмотра: «Готово 2 из 2». */
  get summary(): Locator {
    return this.page.getByText(/^Готово \d+ из \d+/)
  }

  get table(): Locator {
    return this.page.getByRole('table', { name: 'Разобранные записи' })
  }

  /** Строка предпросмотра по номеру в исходном тексте. */
  row(line: number): Locator {
    return this.page.locator(`#import-row-${line}`)
  }

  get createButton(): Locator {
    return this.page.getByRole('button', { name: /^Создать \d+ запис/ })
  }

  get backToFormButton(): Locator {
    return this.page.getByRole('button', { name: 'Изменить список' })
  }

  get report(): Locator {
    return this.page.getByText(/^Перенесено \d+ из \d+/)
  }

  get retryFailedButton(): Locator {
    return this.page.getByRole('button', { name: 'Повторить неудачные' })
  }

  get openBookingsButton(): Locator {
    return this.page.getByRole('button', { name: 'Открыть записи' })
  }

  /** Подсказка «Ближайшее:» после конфликта `409`. */
  slotButton(time: string): Locator {
    return this.page.getByRole('button', { name: time, exact: true })
  }

  async paste(text: string): Promise<void> {
    await this.textarea.fill(text)
  }

  async parse(text: string): Promise<void> {
    await this.paste(text)
    await this.parseButton.click()
    await expect(this.summary).toBeVisible()
  }
}
