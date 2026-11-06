import { expect, test } from '../fixtures/test'

import { accounts, fixtures, storageStatePath } from '../fixtures/accounts'
import { detachBarberSalon } from '../fixtures/api'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'

/**
 * `/barber/salons` — привязка нового салона и создание своего (мутирующий спек,
 * идёт последним).
 *
 * Салона `E2E Studio South` нет ни у одного профиля в сиде: привязка создаёт строку
 * `barber_salons`, которую `finally` удаляет существующим
 * `DELETE /api/barber-profiles/{id}/salons/{salonId}`. Счётчик
 * `barbersUsingActiveCatalogueRows` не меняется: нового usageCount у салона нет,
 * а связь снимается до следующего прогона.
 *
 * Создание своего салона удалить из UI нельзя (эндпоинта нет): строка остаётся
 * одобренной до следующего прогона, а `seed.sql` убирает салоны с владельцем
 * из фикстур вместе со связями и аудитом. На счётчик «выключенные» одобренный
 * салон не влияет: он активен и модерация `Active`.
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

test.describe('мои салоны: создание и модерация', () => {
  test('барбер создаёт салон, админ одобряет, салон появляется в поиске', async ({
    page,
    browser,
  }) => {
    const own = fixtures.ownSalon

    await page.goto('/barber/salons')
    await expect(
      page.getByRole('heading', { name: 'Мои салоны', level: 1 })
    ).toBeVisible()

    await page.getByRole('button', { name: 'Создать салон' }).click()

    const dialog = page.getByRole('dialog')

    await expect(dialog).toBeVisible()
    await expect(
      dialog.getByText('Название нельзя изменить после создания салона.')
    ).toBeVisible()
    await expect(
      dialog.getByText(/Скопируйте координаты из карт/)
    ).toBeVisible()

    await dialog.getByLabel('Название').fill(own.name)
    await dialog.getByLabel('Адрес').fill(own.address)
    await dialog.getByLabel('Город').fill(own.city)
    await dialog.getByLabel('Страна').fill(own.country)
    await dialog.getByLabel('Широта').fill(String(own.latitude))
    await dialog.getByLabel('Долгота').fill(String(own.longitude))
    await dialog.getByLabel('Телефон').fill(own.phoneNumber)
    await dialog.getByLabel('Сайт').fill(own.websiteUrl)

    await dialog.getByRole('button', { name: 'Отправить на проверку' }).click()

    await expect(page.getByText('Салон отправлен на проверку.')).toBeVisible()
    await expect(dialog).toBeHidden()

    const pendingCard = page.getByRole('listitem').filter({ hasText: own.name })

    await expect(pendingCard).toBeVisible()
    await expect(pendingCard.getByText('На проверке')).toBeVisible()
    await expect(pendingCard).toContainText(
      'Салон появится в поиске после проверки администратором.'
    )
    // Салон создан этим барбером: владелец совпадает, правка доступна.
    await expect(
      pendingCard.getByRole('button', { name: 'Изменить адрес' })
    ).toBeVisible()

    // Админ видит салон в очереди проверки и одобряет его.
    const adminContext = await browser.newContext({
      storageState: storageStatePath('admin'),
    })

    try {
      const adminPage = await adminContext.newPage()
      const catalogue = new AdminCataloguePage(adminPage)

      await catalogue.goto('salons')
      await catalogue.waitForRows()
      await catalogue.selectStatus('На проверке')

      const row = catalogue.row(own.name)

      await expect(row).toBeVisible()
      await expect(row).toContainText(accounts.barber.email)

      await catalogue.approveButton(own.name).click()

      const approval = catalogue.actionDialog

      await expect(approval).toBeVisible()
      await expect(approval).toContainText(
        'Салон появится в поиске и станет доступен для записи.'
      )
      await approval.getByRole('button', { name: 'Одобрить' }).click()

      await expect(adminPage.getByRole('status')).toContainText(
        `Салон «${own.name}» одобрен`
      )
      await expect(
        adminPage.getByRole('row').filter({ hasText: own.name })
      ).toHaveCount(0)
    } finally {
      await adminContext.close()
    }

    // «Мои салоны» перечитывает статус после одобрения.
    await page.reload()

    const approvedCard = page.getByRole('listitem').filter({ hasText: own.name })

    await expect(approvedCard).toBeVisible()
    await expect(approvedCard.getByText('Одобрен')).toBeVisible()
    await expect(approvedCard).not.toContainText(
      'Салон появится в поиске после проверки администратором.'
    )

    // Правка владельца предзаполнена настоящими значениями из DTO, без
    // предупреждения о повторном вводе координат и контактов.
    await approvedCard.getByRole('button', { name: 'Изменить контакты' }).click()

    const editDialog = page.getByRole('dialog')

    await expect(editDialog.getByLabel('Страна')).toHaveValue(own.country)
    await expect(editDialog.getByLabel('Широта')).toHaveValue(
      String(own.latitude)
    )
    await expect(editDialog.getByLabel('Телефон')).toHaveValue(own.phoneNumber)
    await expect(editDialog).not.toContainText('не отдаёт')
    await editDialog.getByRole('button', { name: 'Отмена' }).click()
    await expect(editDialog).toBeHidden()

    // Публичный каталог: тот же `GET /api/salons`, что у выбора салона.
    await page.getByRole('button', { name: 'Добавить салон' }).click()

    const picker = page.getByRole('dialog')

    await picker.getByLabel('Поиск').fill(own.name)
    await expect(
      picker.getByRole('radio', { name: new RegExp(own.name) })
    ).toBeVisible()
    await picker.getByRole('button', { name: 'Отмена' }).click()
  })
})
