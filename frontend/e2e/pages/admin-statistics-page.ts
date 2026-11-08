import { type Locator, type Page } from '@playwright/test'

/**
 * `/admin/statistics` — «Статистика сервиса» за период.
 *
 * Период живёт в URL (`?from=&to=&compare=1`), а ответ — `GET /api/admin/statistics`.
 * Плитки и секции опознаются по видимому тексту: спека не завязывается на суммы
 * общего сида, только на структуру и на сам факт нового запроса после смены периода.
 */
export class AdminStatisticsPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  /** Открывает статистику и ждёт ответ API. */
  async goto(query = ''): Promise<void> {
    await Promise.all([
      this.waitForResponse(),
      this.page.goto(`/admin/statistics${query}`),
    ])
  }

  waitForResponse() {
    return this.page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname === '/api/admin/statistics'
        && response.status() === 200
    )
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Статистика', level: 1 })
  }

  get subtitle(): Locator {
    return this.page.getByText(
      'Что происходит за период: записи, клиенты, предложение и качество.'
    )
  }

  get refreshButton(): Locator {
    return this.page.getByRole('button', { name: 'Обновить', exact: true })
  }

  get updatedAt(): Locator {
    return this.page.getByText(/Обновлено в \d{2}:\d{2}/)
  }

  get periodLine(): Locator {
    return this.page.getByText(/дни по UTC/)
  }

  get compareCheckbox(): Locator {
    return this.page.getByRole('checkbox', {
      name: 'Сравнить с прошлым периодом',
    })
  }

  preset(name: string): Locator {
    return this.page.getByRole('button', { name, exact: true })
  }

  get chart(): Locator {
    return this.page.locator('svg[role="img"]')
  }

  get tableToggle(): Locator {
    return this.page.getByText('Показать таблицей')
  }

  get table(): Locator {
    return this.page.getByRole('table', { name: 'Записи и визиты по дням' })
  }

  region(name: string): Locator {
    return this.page.getByRole('region', { name })
  }

  /** Один общий запрос статистики на «Обновить». */
  async refresh(): Promise<void> {
    await Promise.all([this.waitForResponse(), this.refreshButton.click()])
  }
}
