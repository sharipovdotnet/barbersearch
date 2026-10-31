import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { createUpcomingBooking } from '../fixtures/api'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber` — терминальное действие на последней строке страницы (§4.4).
 *
 * Ровно одно действие на прогон: сцена опустошает страницу «Ближайших», поэтому
 * в конце запись создаётся заново — следующий прогон видит тот же набор.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('кабинет барбера: пустая страница после действия', () => {
  test('отмена последней записи страницы уводит на предыдущую, без пустого состояния', async ({
    page,
    playwright,
  }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()

    try {
      // Самовосстановление: если прошлый прогон не успел вернуть запись,
      // возвращаем форму «20 + одна» перед сценарием.
      if ((await cabinet.pager.root.count()) === 0) {
        await createUpcomingBooking(playwright, {
          barberProfileId: fixtures.barberProfileId,
          barberServiceId: fixtures.barberServiceId,
          salonId: fixtures.salonId,
        })
        await page.reload()
        await cabinet.waitForUpcoming()
      }

      await cabinet.pager.clickNext()
      await cabinet.pager.expectRange('21–21')
      await expect(cabinet.rows).toHaveCount(1)

      const row = cabinet.rows.first()
      await row.getByRole('button', { name: 'Отменить' }).click()

      await expect(cabinet.actionDialog).toBeVisible()
      await cabinet.actionDialog.getByRole('button', { name: 'Отменить запись' }).click()
      await expect(cabinet.actionDialog).toBeHidden()

      // Страница опустела: показываем предыдущую, а не «Ближайших записей нет.».
      await expect(cabinet.rows).toHaveCount(20)
      // Курсоры сшиваются: Ближайшие стали одностраничными, пагинатор исчез.
      await expect(cabinet.pager.root).toHaveCount(0)
      await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
      await expect(page.getByText('Ближайших записей нет.')).toHaveCount(0)
    } finally {
      await createUpcomingBooking(playwright, {
        barberProfileId: fixtures.barberProfileId,
        barberServiceId: fixtures.barberServiceId,
        salonId: fixtures.salonId,
      })
    }
  })
})
