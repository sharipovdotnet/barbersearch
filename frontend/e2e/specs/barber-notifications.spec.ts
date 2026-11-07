import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { BarberNotificationsPage } from '../pages/barber-notifications-page'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber/notifications` — инбокс напоминаний: только чтение (§8).
 *
 * Фикстура `seed.sql` кладёт барберу ровно два уведомления: непрочитанное
 * «Upcoming» (14:00, «стрижка», «Барбершоп на Ленина») и прочитанный дайджест
 * (4 записи, первая в 10:00). Отсюда бейдж «1», одна точка непрочитанного и две
 * строки на странице. Настройки — серверные дефолты 08:00/30, Telegram в окружении
 * не настроен (`botConfigured: false`).
 *
 * Мутации («Прочитать все», `leadMinutes = 60`) — в отдельном файле
 * `barber-notifications-mutations.spec.ts`: они меняют эти же фикстуры, поэтому
 * идут последними, один раз, после читающих спек.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('уведомления барбера', () => {
  test('колокольчик кабинета: бейдж «1» и имя со счётчиком', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await expect(cabinet.heading).toBeVisible()

    const bell = cabinet.notificationsBell

    await expect(bell).toBeVisible()
    await expect(bell).toHaveAttribute(
      'aria-label',
      'Уведомления, непрочитанных: 1'
    )
    // Сам бейдж декоративный (`aria-hidden`): счётчик дублируется в имени ссылки.
    await expect(bell.getByText('1', { exact: true })).toBeVisible()
  })

  test('страница: заголовок, обе строки и «Открыть»', async ({ page }) => {
    const notifications = new BarberNotificationsPage(page)

    await notifications.goto()
    await notifications.waitForReady()

    await expect(notifications.heading).toBeVisible()
    await expect(notifications.backLink).toBeVisible()
    await expect(notifications.rows).toHaveCount(2)

    const upcoming = notifications.row('Через 30 минут запись')

    await expect(upcoming).toBeVisible()
    await expect(upcoming).toContainText(
      '14:00 · стрижка · «Барбершоп на Ленина»'
    )
    await expect(upcoming.getByText('Открыть', { exact: true })).toBeVisible()

    const digest = notifications.row('Сегодня 4 записи')

    await expect(digest).toBeVisible()
    await expect(digest).toContainText('Первая в 10:00')
    await expect(digest.getByText('Открыть', { exact: true })).toBeVisible()

    // Непрочитанное — ровно одно: точка стоит только у «Upcoming».
    await expect(notifications.unreadDots).toHaveCount(1)
    await expect(upcoming.getByRole('img', { name: 'Не прочитано' })).toBeVisible()
    await expect(digest.getByRole('img', { name: 'Не прочитано' })).toHaveCount(0)
    await expect(notifications.markAllButton).toBeEnabled()
  })

  test('настройки: дефолты 08:00/30 и выключенный Telegram', async ({ page }) => {
    const notifications = new BarberNotificationsPage(page)

    await notifications.goto()
    await notifications.waitForReady()

    await expect(notifications.settingsSection).toBeVisible()

    await expect(notifications.enabledCheckbox).toBeChecked()
    await expect(notifications.digestCheckbox).toBeChecked()
    await expect(notifications.digestTimeInput).toHaveValue('08:00')
    await expect(notifications.leadSelect).toHaveValue('30')

    // Бот не настроен на сервере: вместо ссылки-кода — объяснение и disabled-кнопка.
    await expect(notifications.telegramNote).toBeVisible()
    await expect(notifications.connectTelegramButton).toBeDisabled()
    await expect(
      page.getByRole('button', { name: 'Отключить' })
    ).toHaveCount(0)
    await expect(
      page.getByRole('link', { name: 'Открыть Telegram' })
    ).toHaveCount(0)
  })
})
