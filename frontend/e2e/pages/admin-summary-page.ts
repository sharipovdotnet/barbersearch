import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/admin/summary` — «Что требует внимания»: четыре блока очередей.
 *
 * Блоки — `<section aria-labelledby>`, поэтому опознаются ролью `region` и именем
 * заголовка. Ссылки внутри блока — настоящие `href`, поэтому спека сравнивает адрес
 * до перехода: сводка обязана ссылаться туда, куда ведёт раздел.
 */
export class AdminSummaryPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  /** Открывает сводку и ждёт реальный ответ `GET api/admin/summary`. */
  async goto(): Promise<void> {
    await Promise.all([
      this.page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === '/api/admin/summary'
          && response.status() === 200
      ),
      this.page.goto('/admin/summary'),
    ])
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Сводка', level: 1 })
  }

  get subtitle(): Locator {
    return this.page.getByText(
      'Что требует внимания. Счётчики считает сервер, разделы открываются ссылками.'
    )
  }

  get refreshButton(): Locator {
    return this.page.getByRole('button', { name: 'Обновить', exact: true })
  }

  get updatedAt(): Locator {
    return this.page.getByText(/Обновлено в \d{2}:\d{2}/)
  }

  /** Блок сводки по имени его `h2`. */
  region(name: string): Locator {
    return this.page.getByRole('region', { name })
  }

  get reviewsBlock(): Locator {
    return this.region('Отзывы на модерации')
  }

  get barbersBlock(): Locator {
    return this.region('Барберы без подтверждения')
  }

  get salonsBlock(): Locator {
    return this.region('Выключенные салоны')
  }

  /** Четвёртый блок #89: очередь премодерации фото. */
  get photosBlock(): Locator {
    return this.region('Фотографии на проверке')
  }

  /** «Все фотографии проверены.» — честный ноль обеих вкладок. */
  get emptyPhotos(): Locator {
    return this.photosBlock.getByText('Все фотографии проверены.', {
      exact: true,
    })
  }

  /** Ссылка в очередь аватаров: адрес по умолчанию — `/admin/photos`. */
  get photosAvatarsLink(): Locator {
    return this.photosBlock.getByRole('link', { name: 'Открыть аватары' })
  }

  /** Ссылка в очередь портфолио: вкладка живёт в `?tab=portfolio`. */
  get photosPortfolioLink(): Locator {
    return this.photosBlock.getByRole('link', { name: 'Открыть портфолио' })
  }

  get oldestReviewLink(): Locator {
    return this.reviewsBlock.getByRole('link', { name: 'Открыть отзыв' })
  }

  get queueLink(): Locator {
    return this.reviewsBlock.getByRole('link', { name: 'К очереди отзывов' })
  }

  get barbersLink(): Locator {
    return this.barbersBlock.getByRole('link', { name: 'Открыть барберов' })
  }

  get salonsLink(): Locator {
    return this.salonsBlock.getByRole('link', { name: 'Открыть каталог салонов' })
  }

  /** «Обновить» и ожидание ровно одного нового ответа сводки. */
  async refresh(): Promise<void> {
    await Promise.all([
      this.page.waitForResponse(
        (response) =>
          new URL(response.url()).pathname === '/api/admin/summary'
          && response.status() === 200
      ),
      this.refreshButton.click(),
    ])
  }

  /**
   * Сводка только читает: кроме «Обновить» и ссылок, действий и графиков нет.
   *
   * Проверка намеренно перечисляет запрещённые подписи: экран не должен получить
   * «Показать ещё», создание, удаление или редактирование даже случайно.
   */
  async expectReadOnly(): Promise<void> {
    for (const name of [
      'Показать ещё',
      'Создать',
      'Удалить',
      'Редактировать',
      'Выключить',
    ]) {
      await expect(
        this.page.getByRole('button', { name, exact: true })
      ).toHaveCount(0)
    }

    await expect(this.page.locator('canvas')).toHaveCount(0)
    await expect(this.page.locator('svg[role="img"]')).toHaveCount(0)
    await expect(this.refreshButton).toHaveCount(1)
  }
}
