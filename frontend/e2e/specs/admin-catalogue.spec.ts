import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'

/**
 * `/admin/catalogue` — чтение: рейл, три вкладки, `?tab=` в URL, фильтр статуса,
 * поиск, `usageCount` и отсутствие пейджера.
 *
 * Фикстуры (`seed.sql`): активная услуга «E2E Стрижка» (её используют 13 профилей),
 * выключенная «E2E Борода», два выключенных салона/специализации. Выключенные строки
 * существуют только для этого экрана — публичные чтения их не отдают.
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('админка: каталог', () => {
  test('рейл ведёт в каталог и отмечает пункт как текущий', async ({ page }) => {
    await page.goto('/admin/reviews')

    await page.getByRole('link', { name: 'Каталог' }).click()

    await expect(page).toHaveURL(/\/admin\/catalogue$/)
    await expect(page.getByRole('link', { name: 'Каталог' })).toHaveAttribute(
      'aria-current',
      'page'
    )
    await expect(
      page.getByRole('heading', { name: 'Каталог', level: 1 })
    ).toBeVisible()
  })

  test('по умолчанию открыты услуги: активная и выключенная строки, счётчик барберов', async ({
    page,
  }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto()
    await catalogue.waitForRows()

    await expect(catalogue.tab('services')).toHaveAttribute('aria-selected', 'true')

    const active = catalogue.row(fixtures.catalogueLabels.activeHairService)
    await expect(active).toHaveCount(1)
    await expect(active).toContainText('Активна')
    // usageCount приходит из API: услугу используют все 13 профилей фикстур.
    await expect(active).toContainText(
      String(fixtures.barbersUsingActiveCatalogueRows)
    )

    const hidden = catalogue.row(fixtures.catalogueLabels.hiddenHairService)
    await expect(hidden).toHaveCount(1)
    await expect(hidden).toContainText('Выключена')
    await expect(hidden).toContainText('0')

    await catalogue.expectNoPager()
  })

  test('вкладка живёт в ?tab= и переживает перезагрузку', async ({ page }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto()
    await catalogue.waitForRows()

    await catalogue.selectTab('salons')

    await expect(page).toHaveURL(/tab=salons/)
    await expect(catalogue.tab('salons')).toHaveAttribute('aria-selected', 'true')
    await expect(
      catalogue.row(fixtures.catalogueLabels.hiddenSalon)
    ).toContainText('Выключен')

    await page.reload()

    await expect(catalogue.tab('salons')).toHaveAttribute('aria-selected', 'true')
    await expect(
      catalogue.row(fixtures.catalogueLabels.hiddenSalon)
    ).toContainText('Выключен')
  })

  test('фильтр статуса оставляет только выключенные или только активные строки', async ({
    page,
  }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto()
    await catalogue.waitForRows()

    await catalogue.selectStatus('Выключенные')

    // Выключенная строка в справочнике одна: остальные строки пришли из minimal-сида.
    await expect(catalogue.rows).toHaveCount(1)
    await expect(
      catalogue.row(fixtures.catalogueLabels.hiddenHairService)
    ).toBeVisible()
    await expect(
      catalogue.row(fixtures.catalogueLabels.activeHairService)
    ).toHaveCount(0)

    await catalogue.selectStatus('Активные')

    await expect(
      catalogue.row(fixtures.catalogueLabels.activeHairService)
    ).toBeVisible()
    await expect(
      catalogue.row(fixtures.catalogueLabels.hiddenHairService)
    ).toHaveCount(0)

    await catalogue.expectNoPager()
  })

  test('поиск сужает список по названию и по городу салона', async ({ page }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto()
    await catalogue.waitForRows()
    await catalogue.searchFor('Борода')

    await expect(catalogue.rows).toHaveCount(1)
    await expect(
      catalogue.row(fixtures.catalogueLabels.hiddenHairService)
    ).toBeVisible()

    await catalogue.selectTab('salons')
    await catalogue.waitForRows()
    await catalogue.searchFor('Санкт')

    await expect(catalogue.rows).toHaveCount(1)
    await expect(catalogue.row(fixtures.catalogueLabels.hiddenSalon)).toBeVisible()
  })

  test('правка салона: название read-only, часовой пояс — select с текущим значением', async ({
    page,
  }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto('salons')
    await catalogue.waitForRows()

    await catalogue.editButton(fixtures.catalogueLabels.hiddenSalon).click()

    const dialog = catalogue.dialog
    await expect(dialog).toBeVisible()
    await expect(dialog.getByLabel('Название')).toHaveValue(
      fixtures.catalogueLabels.hiddenSalon
    )
    await expect(dialog.getByLabel('Название')).toBeDisabled()
    await expect(
      dialog.getByText('Название задаётся при создании.')
    ).toBeVisible()
    await expect(dialog.getByLabel('Часовой пояс')).toHaveValue('Europe/Moscow')

    await dialog.getByRole('button', { name: 'Отмена' }).click()
    await expect(dialog).toHaveCount(0)
  })
})
