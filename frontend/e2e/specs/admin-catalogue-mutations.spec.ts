import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { adminCatalogueMutation } from '../fixtures/api'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'

/**
 * Мутации каталога: выключение с числом барберов, переименование специализации,
 * правка контактов салона двумя запросами и клиентская валидация создания.
 *
 * Каждый сцен возвращает фикстуры в исходное состояние в `finally`: удаления в домене нет,
 * и следующее чтение не должно видеть чужую правку. Создание через диалог здесь
 * не тестируется — удаления нет, и новая строка осталась бы в базе навсегда
 * (create покрыт интеграционными тестами backend и клиентской валидацией формы).
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('админка: каталог, действия', () => {
  test('выключение активной услуги: число барберов, объявление и включение обратно', async ({
    page,
    playwright,
  }) => {
    const catalogue = new AdminCataloguePage(page)
    const label = fixtures.catalogueLabels.activeHairService

    await catalogue.goto()
    await catalogue.waitForRows()

    try {
      await catalogue.toggleButton(label).click()

      const dialog = catalogue.actionDialog
      await expect(dialog).toBeVisible()
      await expect(dialog).toContainText(
        `${fixtures.barbersUsingActiveCatalogueRows} барберов`
      )
      await expect(dialog).toContainText('останется в истории записей')

      await dialog.getByRole('button', { name: 'Выключить' }).click()

      await expect(page.getByRole('status')).toContainText('Услуга выключена')
      await expect(catalogue.row(label)).toContainText('Выключена')
      await expect(
        catalogue.row(label).getByRole('button', { name: 'Включить' })
      ).toBeEnabled()
    } finally {
      // Активация идемпотентна: вызов безопасен, даже если строка уже включена.
      await adminCatalogueMutation(
        playwright,
        'POST',
        `/api/hair-services/${fixtures.activeHairServiceId}/activation`
      )
    }
  })

  test('переименование выключенной специализации', async ({ page, playwright }) => {
    const catalogue = new AdminCataloguePage(page)
    const originalName = fixtures.catalogueLabels.hiddenSpecialization
    const renamed = 'E2E Фейд (после правки)'

    await catalogue.goto('specializations')
    await catalogue.waitForRows()

    try {
      await catalogue.editButton(originalName).click()

      const dialog = catalogue.dialog
      await expect(dialog).toBeVisible()
      await dialog.getByLabel('Название').fill(renamed)
      await dialog.getByRole('button', { name: 'Сохранить' }).click()

      await expect(page.getByRole('status')).toContainText(
        'Специализация переименована'
      )
      await expect(catalogue.row(renamed)).toBeVisible()
    } finally {
      await adminCatalogueMutation(
        playwright,
        'PUT',
        `/api/specializations/${fixtures.hiddenSpecializationIds.fade}`,
        {
          name: originalName,
          description: 'Выключенная специализация E2E-фикстур',
        }
      )
    }
  })

  test('правка контактов салона отправляет оба PUT и показывает новый телефон', async ({
    page,
    playwright,
  }) => {
    const catalogue = new AdminCataloguePage(page)
    const label = fixtures.catalogueLabels.hiddenSalon
    const requests: string[] = []

    page.on('request', (request) => {
      const url = new URL(request.url())

      if (url.pathname.startsWith(`/api/salons/${fixtures.hiddenSalonId}`)) {
        requests.push(`${request.method()} ${url.pathname}`)
      }
    })

    await catalogue.goto('salons')
    await catalogue.waitForRows()

    try {
      await catalogue.editButton(label).click()

      const dialog = catalogue.dialog
      await expect(dialog).toBeVisible()
      await dialog.getByLabel('Телефон').fill('+78125550000')
      await dialog.getByRole('button', { name: 'Сохранить' }).click()

      await expect(page.getByRole('status')).toContainText('Изменения сохранены')
      await expect(catalogue.row(label)).toContainText('+78125550000')

      // Правка салона — два независимых запроса: адрес и контакты.
      expect(requests).toContain(
        `PUT /api/salons/${fixtures.hiddenSalonId}/location`
      )
      expect(requests).toContain(
        `PUT /api/salons/${fixtures.hiddenSalonId}/contact-info`
      )
    } finally {
      await adminCatalogueMutation(
        playwright,
        'PUT',
        `/api/salons/${fixtures.hiddenSalonId}/contact-info`,
        { phoneNumber: '+78120000000', websiteUrl: null }
      )
    }
  })

  test('пустая форма создания показывает клиентские ошибки без запроса', async ({
    page,
  }) => {
    const catalogue = new AdminCataloguePage(page)
    const posts: string[] = []

    page.on('request', (request) => {
      if (request.method() === 'POST') {
        posts.push(request.url())
      }
    })

    await catalogue.goto()
    await catalogue.waitForRows()
    await catalogue.createButton.click()

    const dialog = catalogue.dialog
    await expect(dialog).toBeVisible()
    await dialog.getByRole('button', { name: 'Создать' }).click()

    await expect(dialog.getByText('Введите название услуги.')).toBeVisible()
    // Валидация клиентская: до сервера запрос не дошёл.
    expect(posts).toStrictEqual([])

    await dialog.getByRole('button', { name: 'Отмена' }).click()
    await expect(dialog).toHaveCount(0)
  })
})
