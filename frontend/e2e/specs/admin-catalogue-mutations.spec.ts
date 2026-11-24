import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { adminCatalogueMutation } from '../fixtures/api'
import { mockAddressSuggestions } from '../fixtures/suggestions'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'

/**
 * Мутации каталога: выключение с числом барберов, правка услуги с обязательной
 * специализацией, переименование специализации с обязательной категорией, правка
 * контактов салона двумя запросами и клиентская валидация создания.
 *
 * Каждый сцен возвращает фикстуры в исходное состояние в `finally`: удаления в домене нет,
 * и следующее чтение не должно видеть чужую правку. Создание через диалог здесь
 * не тестируется — удаления нет, и новая строка осталась бы в базе навсегда
 * (create покрыт интеграционными тестами backend и клиентской валидацией формы).
 * Тела PUT проверяются на настоящих запросах: у услуги больше нет `category`,
 * вместо неё обязательный `specializationId`, а у специализации появилась `category`.
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

  test('переименование выключенной специализации: категория в селекте и в теле PUT', async ({
    page,
    playwright,
  }) => {
    const catalogue = new AdminCataloguePage(page)
    const originalName = fixtures.catalogueLabels.fadeSpecialization
    const renamed = 'E2E Фейд (после правки)'
    const path = `/api/specializations/${fixtures.hiddenSpecializationIds.fade}`
    // Категория уходит **именем**, а не числом: API читает перечисления строками
    // (`JsonStringEnumConverter(allowIntegerValues: false)`), числовой `1` — это 400
    // до обработчика. В базе у `E2E Фейд` при этом лежит `category = 1` («Стрижка»).
    const original = {
      name: originalName,
      category: 'Haircut',
      description: 'Выключенная специализация E2E-фикстур',
    }

    await catalogue.goto('specializations')
    await catalogue.waitForRows()

    try {
      await catalogue.editButton(originalName).click()

      const dialog = catalogue.dialog
      await expect(dialog).toBeVisible()
      // Категория обязательна и предзаполнена текущей: у «E2E Фейд» — «Стрижка».
      await expect(dialog.getByLabel('Категория')).toHaveValue('Haircut')
      await expect(
        dialog.getByLabel('Категория').locator('option:checked')
      ).toHaveText('Стрижка')
      await expect(
        dialog.getByText(
          'Категория специализации определяет фильтр витрины и категорию её услуг.'
        )
      ).toBeVisible()

      await dialog.getByLabel('Название').fill(renamed)

      const requestPromise = page.waitForRequest(
        (request) =>
          request.method() === 'PUT' &&
          new URL(request.url()).pathname === path
      )
      await dialog.getByRole('button', { name: 'Сохранить' }).click()

      // Без категории в теле сервер ответил бы 400: это обязательное поле.
      expect((await requestPromise).postDataJSON()).toStrictEqual({
        ...original,
        name: renamed,
      })

      await expect(page.getByRole('status')).toContainText(
        'Специализация переименована'
      )
      await expect(catalogue.row(renamed)).toBeVisible()
    } finally {
      await adminCatalogueMutation(playwright, 'PUT', path, original)
    }
  })

  test('правка услуги: селект специализации предзаполнен, тело PUT несёт specializationId', async ({
    page,
    playwright,
  }) => {
    const catalogue = new AdminCataloguePage(page)
    const label = fixtures.catalogueLabels.hiddenHairService
    const path = `/api/hair-services/${fixtures.hiddenHairServiceId}`
    // Тело правки без изменений: `category` у услуги больше нет, вместо неё
    // обязательный `specializationId`, а категория приходит из специализации.
    const payload = {
      name: label,
      description: 'Выключенная услуга E2E-фикстур',
      defaultDurationMinutes: 45,
      specializationId: fixtures.hiddenSpecializationIds.beard,
    }

    await catalogue.goto()
    await catalogue.waitForRows()

    try {
      await catalogue.editButton(label).click()

      const dialog = catalogue.dialog
      await expect(dialog).toBeVisible()

      const select = dialog.getByLabel('Специализация')
      await expect(select).toHaveValue(fixtures.hiddenSpecializationIds.beard)
      // Выключенная специализация остаётся в селекте с пометкой: иначе правка старой
      // услуги потеряла бы текущее значение.
      await expect(select.locator('option:checked')).toHaveText(
        `${label} (выключена)`
      )
      await expect(
        dialog.getByText('Категория определяется специализацией: Борода')
      ).toBeVisible()

      const requestPromise = page.waitForRequest(
        (request) =>
          request.method() === 'PUT' &&
          new URL(request.url()).pathname === path
      )
      await dialog.getByRole('button', { name: 'Сохранить' }).click()

      expect((await requestPromise).postDataJSON()).toStrictEqual(payload)
      await expect(page.getByRole('status')).toContainText('Изменения сохранены')
    } finally {
      await adminCatalogueMutation(playwright, 'PUT', path, payload)
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
      await dialog.getByLabel('Телефон').fill('+79991234567')
      await dialog.getByRole('button', { name: 'Сохранить' }).click()

      await expect(page.getByRole('status')).toContainText('Изменения сохранены')
      await expect(catalogue.row(label)).toContainText('+79991234567')

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
        { phoneNumber: '+79991234571', websiteUrl: null }
      )
    }
  })

  /**
   * Подсказки города и адреса: провайдер по умолчанию выключен (`Enabled=false`),
   * поэтому оба ответа подменяются через `page.route`. Проверяем, что выбор подсказки
   * доходит до тела `PUT` без `providerId` и `region`.
   */
  test('подсказки адреса заполняют поля и не отправляют providerId', async ({
    page,
    playwright,
  }) => {
    const catalogue = new AdminCataloguePage(page)
    const label = fixtures.catalogueLabels.hiddenSalon
    const salonPath = `/api/salons/${fixtures.hiddenSalonId}/location`
    const original = {
      address: 'ул. Северная, 2',
      city: 'Санкт-Петербург',
      country: 'Russia',
      postalCode: '190000',
      timeZoneId: 'Europe/Moscow',
      latitude: 59.9386,
      longitude: 30.3141,
    }

    // Те же заглушки, что и в `salon-suggestions.spec.ts`: провайдер по умолчанию
    // выключен (`Enabled=false`), а данные обязаны проходить `response-guards`.
    await mockAddressSuggestions(page)

    await catalogue.goto('salons')
    await catalogue.waitForRows()

    try {
      await catalogue.editButton(label).click()

      const dialog = catalogue.dialog
      await expect(dialog).toBeVisible()

      await dialog.getByLabel('Город').fill('Екат')
      // Popup живёт в портале на `body`, а не внутри диалога: ищем пункты на странице.
      // `exact` обязателен: в общих заглушках есть ещё «г. Екатеринбург, пос. Исток».
      await page
        .getByRole('option', { name: 'г. Екатеринбург', exact: true })
        .click()

      await dialog.getByLabel('Адрес', { exact: true }).fill('ул. Лен')
      await page
        .getByRole('option', { name: 'г. Екатеринбург, ул. Ленина, д. 1' })
        .click()

      await expect(dialog.getByLabel('Почтовый индекс')).toHaveValue('620000')
      await expect(dialog.getByLabel('Широта')).toHaveValue('56.8389')

      const requestPromise = page.waitForRequest(
        (request) =>
          request.method() === 'PUT' &&
          new URL(request.url()).pathname === salonPath
      )

      await dialog.getByRole('button', { name: 'Сохранить' }).click()

      const body = (await requestPromise).postDataJSON()

      expect(body).toMatchObject({
        address: 'ул. Ленина, 1',
        city: 'Екатеринбург',
        postalCode: '620000',
        latitude: 56.8389,
        longitude: 60.5975,
        timeZoneId: 'Europe/Moscow',
      })
      expect(body).not.toHaveProperty('providerId')
      expect(body).not.toHaveProperty('region')
      await expect(page.getByText('Изменения сохранены')).toBeVisible()
    } finally {
      await adminCatalogueMutation(playwright, 'PUT', salonPath, original)
    }
  })

  test('пустая форма создания услуги: ошибка названия без запроса', async ({
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

    /*
     * Специализация обязательна, но в непустом справочнике первая строка выбирается
     * сразу (`effectiveSpecializationId = specializationId || specializations[0]?.id`),
     * поэтому «Выберите специализацию для услуги.» через интерфейс здесь недостижима:
     * пустой справочник, единственное состояние с пустым селектом, делает кнопку
     * «Создать» disabled. Эту ветку проверяет `hair-service-dialog.test.tsx`.
     */
    const specialization = dialog.getByLabel('Специализация')
    await expect(specialization).toBeEnabled()
    await expect(specialization).not.toHaveValue('')
    // Категория услуги производная: вместо поля — подсказка о категории специализации.
    await expect(
      dialog.getByText(/^Категория определяется специализацией: /)
    ).toBeVisible()

    await dialog.getByRole('button', { name: 'Создать' }).click()

    await expect(dialog.getByText('Введите название услуги.')).toBeVisible()
    // Валидация клиентская: до сервера запрос не дошёл.
    expect(posts).toStrictEqual([])

    await dialog.getByRole('button', { name: 'Отмена' }).click()
    await expect(dialog).toHaveCount(0)
  })
})
