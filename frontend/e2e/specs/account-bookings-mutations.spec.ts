import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { createUpcomingBooking } from '../fixtures/api'
import { AccountBookingsPage } from '../pages/account-bookings-page'

/**
 * `/account` — отмена ближайшей записи (§4.6).
 *
 * Отмена необратима, поэтому в конце сценария запись создаётся заново на того же
 * барбера B2: следующий прогон видит тот же набор, а счётчики кабинета барбера B1
 * не меняются. Отменённая строка остаётся в истории — это ожидаемо.
 */
test.use({ storageState: storageStatePath('customer') })

test.describe('мои записи: отмена уводит строку в историю', () => {
  test('первая страница предстоящих уменьшается до 19, история начинается с отменённой', async ({
    page,
    playwright,
  }) => {
    const account = new AccountBookingsPage(page)

    await account.goto()
    await account.waitForUpcoming()

    try {
      await expect(account.hero).toContainText('Барбер Номер 1')

      await account.hero
        .getByRole('button', { name: 'Отменить', exact: true })
        .click()

      await expect(account.cancelDialog).toBeVisible()
      await account.cancelDialog
        .getByRole('button', { name: 'Отменить запись' })
        .click()
      await expect(account.cancelDialog).toBeHidden()

      // Оптимистичных правок нет: сначала 204, потом строка уходит из списка.
      await expect(
        page.getByRole('status').filter({ hasText: 'отменена' })
      ).toBeVisible()

      // Строку со второй страницы хук не подтягивает: на первой остаётся 19.
      await expect(account.rows).toHaveCount(19)
      await account.upcomingPager.expectRange('1–19')

      // Ближайшая запись — теперь n = 2 (B3), отменённая ушла из «Предстоящих».
      await expect(account.hero).toContainText('Барбер Номер 2')

      // Курсор на вторую страницу остался: «Дальше» по-прежнему открывает одну строку.
      await account.upcomingPager.clickNext()
      await account.upcomingPager.expectRange('20–20')
      await expect(account.rows).toHaveCount(1)

      // История сортируется по убыванию: отменённая запись (завтра) — первая.
      await account.openTab('История')
      await expect(account.rows).toHaveCount(20)

      const firstHistoryRow = account.rows.first()

      await expect(firstHistoryRow).toContainText('Отменена')
      await expect(firstHistoryRow).toContainText('Барбер Номер 1')
    } finally {
      // Восстановление видимого числа: тот же барбер и услуга, что у отменённой (B2).
      await createUpcomingBooking(playwright, {
        barberProfileId: fixtures.customerBarberProfileId,
        barberServiceId: fixtures.customerBarberServiceId,
        salonId: fixtures.salonId,
      })
    }
  })
})
