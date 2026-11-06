import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'

/**
 * `/barber/salons` — «Мои салоны» (§4.4).
 *
 * Читающий спек: у витринного барбера один основной салон `E2E Studio`, он и должен
 * прийти из `GET /api/barber-profiles/{id}/salons`. Привязка нового салона меняет
 * `barber_salons` и живёт в отдельном мутирующем спеке.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('мои салоны', () => {
  test('салон, основной бейдж, третий пункт навигации и поиск каталога', async ({
    page,
  }) => {
    await page.goto('/barber/salons')

    await expect(
      page.getByRole('heading', { name: 'Мои салоны', level: 1 })
    ).toBeVisible()

    const salonCard = page.getByRole('listitem').filter({
      hasText: fixtures.catalogueLabels.activeSalon,
    })

    await expect(salonCard).toBeVisible()
    await expect(salonCard.getByText('Основной', { exact: true })).toBeVisible()
    await expect(salonCard).toContainText('Москва')
    await expect(salonCard).toContainText('ул. Тестовая, 1')
    await expect(salonCard).toContainText('Europe/Moscow')
    // Салон создан администратором: владельца у него нет, правки барберу недоступны.
    await expect(
      salonCard.getByRole('button', { name: 'Изменить адрес' })
    ).toHaveCount(0)

    await expect(page.getByRole('link', { name: 'Записи' })).toHaveAttribute(
      'href',
      '/barber'
    )
    await expect(
      page.getByRole('link', { name: 'Расписание' })
    ).toHaveAttribute('href', '/barber/schedule')
    await expect(page.getByRole('link', { name: 'Салоны' })).toHaveAttribute(
      'href',
      '/barber/salons'
    )
    await expect(page.getByRole('link', { name: 'Салоны' })).toHaveAttribute(
      'aria-current',
      'page'
    )

    // Каталог открыт анонимно, но диалог здесь доступен только после гейтов.
    await page.getByRole('button', { name: 'Добавить салон' }).click()

    const dialog = page.getByRole('dialog')

    await expect(dialog).toBeVisible()
    await dialog.getByLabel('Поиск').fill('South')
    await expect(dialog.getByRole('radio')).toHaveCount(1)

    await dialog.getByRole('button', { name: 'Отмена' }).click()
    await expect(dialog).toBeHidden()
  })
})
