import { expect, type Locator, type Page } from '@playwright/test'

/**
 * Общий пагинатор списка (`src/components/list-pager.tsx`).
 *
 * Один класс на все списки: разметка и доступные имена одинаковые, отличается
 * только `aria-label` навигации. Копий проверок по спекам быть не должно (§3.6).
 */
export class ListPagerComponent {
  readonly page: Page
  readonly root: Locator

  constructor(page: Page, label: string) {
    this.page = page
    this.root = page.getByRole('navigation', { name: label })
  }

  /** Кнопка «Назад»: `aria-label` не зависит от ширины экрана. */
  get previous(): Locator {
    return this.root.getByRole('button', { name: 'Предыдущая страница' })
  }

  /** Кнопка «Дальше»; при ошибке страницы она же становится повтором. */
  get next(): Locator {
    return this.root.getByRole('button', { name: 'Следующая страница' })
  }

  get retry(): Locator {
    return this.root.getByRole('button', { name: 'Повторить загрузку' })
  }

  /** Видимый диапазон: `1–10 из 12` или `21–40`, если сервер не отдаёт total. */
  get range(): Locator {
    return this.root.locator('div p')
  }

  /** Скрытый живой регион: `Страницы отзывов: 1–10 из 12`. */
  get announcement(): Locator {
    return this.root.locator('p[aria-live="polite"]')
  }

  get alert(): Locator {
    return this.root.locator('[role="alert"]')
  }

  async expectRange(text: string): Promise<void> {
    await expect(this.range).toHaveText(text)
  }

  /**
   * Диапазон без общего числа: сервер не отдаёт total, поэтому проверяем
   * только «начало–конец» и число строк на странице (§4.5).
   *
   * Читает текущее значение без ожидания — годится там, где страница не меняется.
   */
  async rangeBounds(): Promise<{ from: number; to: number }> {
    const text = (await this.range.innerText()).trim()
    const match = /^(\d+)–(\d+)$/.exec(text)

    expect(match, `диапазон без «из N», получено «${text}»`).not.toBeNull()

    const [, from, to] = match as RegExpExecArray

    return { from: Number(from), to: Number(to) }
  }

  /**
   * Ждёт, пока диапазон начнётся с `from`, и возвращает границы.
   *
   * `toHaveText` с регуляркой ретраится: после клика по кнопке сеть ещё в пути,
   * а разовое чтение вернуло бы старый диапазон.
   */
  async expectRangeFrom(from: number): Promise<{ from: number; to: number }> {
    await expect(
      this.range,
      `диапазон начинается с ${from} и не содержит «из N»`
    ).toHaveText(new RegExp(`^${from}–\\d+$`))

    const text = (await this.range.innerText()).trim()
    const match = /^(\d+)–(\d+)$/.exec(text) as RegExpExecArray

    return { from: Number(match[1]), to: Number(match[2]) }
  }

  async clickNext(): Promise<void> {
    await this.next.click()
  }

  async clickPrevious(): Promise<void> {
    await this.previous.click()
  }
}
