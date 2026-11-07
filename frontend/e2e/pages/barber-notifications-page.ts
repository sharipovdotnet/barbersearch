import { expect, type Locator, type Page } from '@playwright/test'

/**
 * `/barber/notifications` — инбокс напоминаний барбера (§8).
 *
 * Экран открывается по колокольчику из кабинета, а не из навигации: сам маршрут
 * не входит в `BarberCabinetNav`. Сверху настройки (check-боксы, время дайджеста,
 * «Напомнить за» и блок Telegram), ниже — курсорный список уведомлений.
 *
 * Фикстура `seed.sql` кладёт ровно две строки: непрочитанное «Upcoming» (14:00,
 * «стрижка», «Барбершоп на Ленина») и прочитанный дайджест (4 записи, первая в 10:00).
 * Поэтому бейдж колокольчика показывает «1», а список — обе строки.
 */
export class BarberNotificationsPage {
  readonly page: Page

  constructor(page: Page) {
    this.page = page
  }

  async goto(): Promise<void> {
    await this.page.goto('/barber/notifications')
  }

  get heading(): Locator {
    return this.page.getByRole('heading', { name: 'Уведомления', level: 1 })
  }

  get backLink(): Locator {
    return this.page.getByRole('link', { name: 'Записи клиентов' })
  }

  get markAllButton(): Locator {
    return this.page.getByRole('button', { name: 'Прочитать все' })
  }

  /** Подтверждение «Прочитать все»: `role="status"`, появляется после `204`. */
  get announcement(): Locator {
    return this.page
      .getByRole('status')
      .filter({ hasText: 'Все уведомления прочитаны.' })
  }

  /** «Настройки» — секция с `aria-labelledby`; внутри форма и блок Telegram. */
  get settingsSection(): Locator {
    return this.page.getByRole('region', { name: 'Настройки' })
  }

  get enabledCheckbox(): Locator {
    return this.page.getByRole('checkbox', { name: 'Присылать напоминания' })
  }

  get digestCheckbox(): Locator {
    return this.page.getByRole('checkbox', { name: 'Утренний дайджест' })
  }

  get digestTimeInput(): Locator {
    return this.page.getByLabel('Время утреннего дайджеста')
  }

  get leadSelect(): Locator {
    return this.page.getByLabel('Напомнить за')
  }

  get telegramNote(): Locator {
    return this.page.getByText(
      'Telegram не настроен. Уведомления будут видны только здесь.'
    )
  }

  get connectTelegramButton(): Locator {
    return this.page.getByRole('button', { name: 'Подключить' })
  }

  /** Секция списка: `<section aria-label="Уведомления">`. */
  get listSection(): Locator {
    return this.page.locator('section[aria-label="Уведомления"]')
  }

  get list(): Locator {
    return this.listSection.getByRole('list')
  }

  /** Строки инбокса; каждая — кнопка с заголовком, телом и «Открыть». */
  get rows(): Locator {
    return this.list.getByRole('listitem')
  }

  /** Точки непрочитанного (`role="img"`, имя «Не прочитано»). */
  get unreadDots(): Locator {
    return this.list.getByRole('img', { name: 'Не прочитано' })
  }

  /** Строка по началу заголовка: «Через 30 минут запись» или «Сегодня 4 записи». */
  row(title: string | RegExp): Locator {
    return this.rows.filter({ hasText: title })
  }

  /**
   * Ждёт загруженные настройки и обе строки: скелетоны исчезли.
   *
   * Так же, как соседние page objects, сцена сначала дожидается устойчивого
   * состояния, а не снимает DOM во время загрузки (axe-сканы, «Прочитать все»).
   */
  async waitForReady(): Promise<void> {
    await expect(this.heading).toBeVisible()
    await expect(this.enabledCheckbox).toBeVisible()
    await expect(this.rows).toHaveCount(2)
  }
}
