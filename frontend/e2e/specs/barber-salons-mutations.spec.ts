import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { detachBarberSalon } from '../fixtures/api'

/**
 * `/barber/salons` — привязка нового салона (мутирующий спек, идёт последним).
 *
 * Салона `E2E Studio South` нет ни у одного профиля в сиде: привязка создаёт строку
 * `barber_salons`, которую `finally` удаляет существующим
 * `DELETE /api/barber-profiles/{id}/salons/{salonId}`. Счётчик
 * `barbersUsingActiveCatalogueRows` не меняется: нового usageCount у салона нет,
 * а связь снимается до следующего прогона.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('мои салоны: привязка', () => {
  test('новый салон добавляется, перечитывается и удаляется в finally', async ({
    page,
    playwright,
  }) => {
    try {
      await page.goto('/barber/salons')
      await expect(
        page.getByRole('heading', { name: 'Мои салоны', level: 1 })
      ).toBeVisible()

      await page.getByRole('button', { name: 'Добавить салон' }).click()

      const dialog = page.getByRole('dialog')

      await expect(dialog).toBeVisible()
      await dialog.getByLabel('Поиск').fill('South')
      await dialog.getByRole('radio', { name: /E2E Studio South/ }).check()

      await dialog.getByRole('button', { name: 'Добавить' }).click()

      await expect(page.getByText('Салон добавлен.')).toBeVisible()
      await expect(dialog).toBeHidden()

      const newCard = page.getByRole('listitem').filter({
        hasText: fixtures.attachSalonName,
      })

      await expect(newCard).toBeVisible()
      await expect(newCard).toContainText('ул. Южная, 3')

      // Перечитывание после записи: карточка остаётся после перезагрузки.
      await page.reload()
      await expect(
        page.getByRole('listitem').filter({ hasText: fixtures.attachSalonName })
      ).toBeVisible()
    } finally {
      await detachBarberSalon(playwright, {
        barberProfileId: fixtures.barberProfileId,
        salonId: fixtures.attachSalonId,
      })
    }
  })

  test('повторная привязка показывает 409 и не закрывает диалог', async ({ page }) => {
    await page.goto('/barber/salons')
    await expect(
      page.getByRole('heading', { name: 'Мои салоны', level: 1 })
    ).toBeVisible()

    await page.getByRole('button', { name: 'Добавить салон' }).click()

    const dialog = page.getByRole('dialog')

    await dialog.getByLabel('Поиск').fill('E2E Studio')
    await dialog.getByRole('radio', { name: /E2E Studio Москва/ }).check()
    await dialog.getByRole('button', { name: 'Добавить' }).click()

    await expect(dialog.getByText('Этот салон уже добавлен.')).toBeVisible()
    await expect(dialog).toBeVisible()
  })
})
