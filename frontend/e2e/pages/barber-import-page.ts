import { Buffer } from 'node:buffer'

import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/barber/import` — перенос старых записей из календаря (`.ics`) или списка.
 *
 * Экран живёт тремя шагами на одном адресе: форма, проверка и отчёт. Объект
 * страницы называет каждый шаг своим локатором, чтобы спек читался как путь
 * барбера, а не как набор CSS-селекторов. Источник переключается нативными
 * радио: по умолчанию — файл календаря, текст выбирается явно.
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

  /** Группа «Откуда перенести»: нативные радио в `fieldset`/`legend`. */
  get sourceGroup(): Locator {
    return this.page.getByRole('group', { name: 'Откуда перенести' })
  }

  get calendarSource(): Locator {
    return this.page.getByRole('radio', { name: 'Файл календаря', exact: true })
  }

  get textSource(): Locator {
    return this.page.getByRole('radio', { name: 'Список текстом', exact: true })
  }

  /**
   * Скрытый `input[type=file]` — только по типу, не по имени.
   *
   * У радио «Файл календаря» и у поля файла одно доступное имя, поэтому
   * `getByRole`/`getByLabel` по имени задел бы оба элемента.
   */
  get fileInput(): Locator {
    return this.page.locator('form input[type="file"]')
  }

  get parseFileButton(): Locator {
    return this.page.getByRole('button', { name: 'Разобрать файл', exact: true })
  }

  /** Период разбора календаря: 7 / 30 / 90 дней, по умолчанию 30. */
  get periodSelect(): Locator {
    return this.page.getByLabel('Период', { exact: true })
  }

  /** Сводка календаря: «Выбрано 1 из 1 · Готово 1». */
  get icsSummary(): Locator {
    return this.page.getByText(/^Выбрано \d+ из \d+/)
  }

  /** «Пропущено N событий» — раскрывающийся блок над предпросмотром. */
  get skipped(): Locator {
    return this.page.getByText(/^Пропущено \d+ событ/)
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

  /** Переключение на источник «Список текстом»: без него поля вставки нет. */
  async selectTextSource(): Promise<void> {
    await this.textSource.check()
  }

  async selectCalendarSource(): Promise<void> {
    await this.calendarSource.check()
  }

  /**
   * Кладёт файл в скрытый input: `setInputFiles` не требует видимости поля.
   *
   * Имя файла показывает сама форма, поэтому `.ics` передаётся явно, а не
   * выводится из содержимого.
   */
  async uploadCalendar(
    content: string | Buffer,
    name: string
  ): Promise<void> {
    await this.fileInput.setInputFiles({
      name,
      mimeType: 'text/calendar',
      buffer: typeof content === 'string' ? Buffer.from(content, 'utf8') : content,
    })
  }

  async parseCalendar(): Promise<void> {
    await this.parseFileButton.click()
    await expect(this.icsSummary).toBeVisible()
  }

  async parse(text: string): Promise<void> {
    await this.paste(text)
    await this.parseButton.click()
    await expect(this.summary).toBeVisible()
  }
}
