import type { Locator, Page } from '@playwright/test'

import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import {
  expectActiveSuggestion,
  expectClosedSuggestionList,
  expectOpenSuggestionList,
  mockAddressSuggestions,
  SUGGESTION_ADDRESSES,
  SUGGESTION_CITIES,
  type CitySuggestionMock,
} from '../fixtures/suggestions'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'

/**
 * Подсказки города и адреса в настоящем браузере (§11, §15).
 *
 * jsdom не видит главного: popup живёт в портале на уровне диалога, поэтому его
 * нельзя обрезать `overflow` формы, а кликам и `.fill()` в соседних полях он не
 * мешает. Здесь же проверяются клавиатурный путь Base UI Combobox, ARIA-связь
 * поля со списком и позиционирование на 1280 и 320 px.
 *
 * Провайдер подсказок в тестах выключен, поэтому оба эндпоинта подменяются
 * через `page.route` (`e2e/fixtures/suggestions.ts`) — как в сцене
 * `admin-catalogue-mutations.spec.ts`. Сценарии только читают: форма не
 * отправляется, фикстуры не меняются, `finally` не нужен.
 *
 * Ширины заданы явно. Проект `mobile-chromium` (375 px) сцены не дублирует:
 * на телефоне та же вёрстка, что на 1280, а узкий экран проверяется на 320.
 */
test.skip(
  ({ isMobile }) => isMobile,
  'ширины 1280 и 320 задаются явно: 375 px не добавляет покрытия'
)

/** Админский каталог: форма правки выключенного салона. */
async function openSalonEditDialog(page: Page): Promise<Locator> {
  const catalogue = new AdminCataloguePage(page)

  await catalogue.goto('salons')
  await catalogue.waitForRows()
  await catalogue.editButton(fixtures.catalogueLabels.hiddenSalon).click()

  const dialog = page.getByRole('dialog')

  await expect(dialog).toBeVisible()

  return dialog
}

/** Кабинет барбера: форма создания своего салона. */
async function openOwnerSalonDialog(page: Page): Promise<Locator> {
  await page.goto('/barber/salons')
  await expect(
    page.getByRole('heading', { name: 'Мои салоны', level: 1 })
  ).toBeVisible()

  await page.getByRole('button', { name: 'Создать салон' }).click()

  const dialog = page.getByRole('dialog')

  await expect(dialog).toBeVisible()

  return dialog
}

/** Попап подсказок не должен вылезать за вьюпорт ни одной стороной. */
async function expectInsideViewport(page: Page, element: Locator): Promise<void> {
  const box = await element.boundingBox()
  const viewport = page.viewportSize()

  expect(box, 'у попапа есть геометрия').not.toBeNull()
  expect(viewport, 'у проекта есть viewport').not.toBeNull()

  if (box === null || viewport === null) {
    return
  }

  expect(box.x).toBeGreaterThanOrEqual(0)
  expect(box.y).toBeGreaterThanOrEqual(0)
  expect(box.x + box.width).toBeLessThanOrEqual(viewport.width + 0.5)
  expect(box.y + box.height).toBeLessThanOrEqual(viewport.height + 0.5)
}

/** Попап не создаёт горизонтального скролла страницы (§11). */
async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(
    () =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth
  )

  expect(overflow).toBeLessThanOrEqual(0)
}

test.describe('подсказки салона на 1280', () => {
  test.use({
    storageState: storageStatePath('admin'),
    viewport: { width: 1280, height: 900 },
  })

  test('Escape и blur закрывают список и не стирают текст', async ({ page }) => {
    await mockAddressSuggestions(page)

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')
    const country = dialog.getByLabel('Страна')

    await city.fill('Екат')

    const listbox = await expectOpenSuggestionList(
      page,
      city,
      'Подсказки города'
    )

    await expect(
      page.getByRole('option', { name: 'г. Екатеринбург', exact: true })
    ).toBeVisible()
    // До стрелок активного пункта нет: подсветка появляется только по навигации.
    expect(await city.getAttribute('aria-activedescendant')).toBeNull()

    await city.press('Escape')

    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(city).toBeFocused()
    await expect(city).toHaveValue('Екат')
    // Подсказка не выбрана: страна осталась из фикстуры, а не «Россия».
    await expect(country).toHaveValue('Russia')

    // Строка не изменилась, но стрелка снова открывает список.
    await city.press('ArrowDown')

    await expect(listbox).toBeVisible()

    await country.click()

    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(country).toBeFocused()
    await expect(city).toHaveValue('Екат')
  })

  test('соседнее поле принимает ввод и клик при открытом списке', async ({
    page,
  }) => {
    await mockAddressSuggestions(page)

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')
    const country = dialog.getByLabel('Страна')

    await city.fill('Екат')
    await expectOpenSuggestionList(page, city, 'Подсказки города')

    // `.fill()` в соседнее поле не перехватывается попапом: фокус уходит,
    // список закрывается, введённый город остаётся.
    await country.fill('Россия')

    await expect(country).toHaveValue('Россия')
    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(city).toHaveValue('Екат')

    await city.press('ArrowDown')
    await expectOpenSuggestionList(page, city, 'Подсказки города')

    await country.click()

    await expect(country).toBeFocused()
    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(city).toHaveValue('Екат')
  })

  test('Tab уходит в соседнее поле без выбора и без потери текста', async ({
    page,
  }) => {
    await mockAddressSuggestions(page)

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')
    const country = dialog.getByLabel('Страна')
    const postalCode = dialog.getByLabel('Почтовый индекс')

    await city.fill('Екат')
    await expectOpenSuggestionList(page, city, 'Подсказки города')

    await city.press('Tab')

    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(country).toBeFocused()
    await expect(city).toHaveValue('Екат')
    await expect(country).toHaveValue('Russia')
    await expect(dialog).toBeVisible()

    // Поле остаётся в обычном Tab-порядке: попап не запирает фокус.
    await page.keyboard.press('Tab')
    await expect(postalCode).toBeFocused()
    await page.keyboard.press('Shift+Tab')
    await page.keyboard.press('Shift+Tab')
    await expect(city).toBeFocused()

    // Клавиатурный фокус виден: компонент сохраняет ring (§11).
    const focusRing = await city.evaluate((element) => {
      const style = getComputedStyle(element)

      return `${style.outlineStyle} ${style.boxShadow}`
    })

    expect(focusRing).toContain('3px')
  })

  test('клавиатура: стрелки, Home/End, Space и Enter', async ({ page }) => {
    await mockAddressSuggestions(page)

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')
    const country = dialog.getByLabel('Страна')
    const writes: string[] = []

    page.on('request', (request) => {
      if (request.method() === 'PUT' || request.method() === 'POST') {
        writes.push(`${request.method()} ${new URL(request.url()).pathname}`)
      }
    })

    await city.fill('Екат')

    const listbox = await expectOpenSuggestionList(
      page,
      city,
      'Подсказки города'
    )

    await city.press('ArrowDown')
    await expectActiveSuggestion(page, city, 'г. Екатеринбург')

    await city.press('ArrowDown')
    await expectActiveSuggestion(page, city, 'г. Екатеринбург, пос. Исток')

    await city.press('ArrowUp')
    await expectActiveSuggestion(page, city, 'г. Екатеринбург')

    // Home/End у typeable combobox двигают каретку текста, а не пункт списка:
    // подсветка не меняется, значение и открытый список сохраняются.
    await city.press('End')
    expect(
      await city.evaluate((input) => (input as HTMLInputElement).selectionEnd)
    ).toBe('Екат'.length)
    await expectActiveSuggestion(page, city, 'г. Екатеринбург')

    await city.press('Home')
    expect(
      await city.evaluate((input) => (input as HTMLInputElement).selectionStart)
    ).toBe(0)
    await expectActiveSuggestion(page, city, 'г. Екатеринбург')
    await expect(listbox).toBeVisible()

    // Space — это пробел в строке, а не выбор: подсветка снимается, список открыт.
    await city.press('End')
    await city.press(' ')
    await expect(city).toHaveValue('Екат ')
    expect(await city.getAttribute('aria-activedescendant')).toBeNull()
    await expect(listbox).toBeVisible()
    await expect(country).toHaveValue('Russia')

    // Enter по подсвеченному пункту выбирает его: третий город — Казань.
    await city.fill('')
    await city.fill('Екат')
    await expectOpenSuggestionList(page, city, 'Подсказки города')
    await city.press('ArrowDown')
    await city.press('ArrowDown')
    await city.press('ArrowDown')
    await expectActiveSuggestion(page, city, 'г. Казань')

    await city.press('Enter')

    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(city).toHaveValue('Казань')
    await expect(country).toHaveValue('Россия')
    await expect(dialog).toBeVisible()

    // Enter по подсветке адреса заполняет улицу, индекс и координаты;
    // часовой пояс подсказка не трогает.
    const address = dialog.getByLabel('Адрес', { exact: true })

    await address.fill('ул. Лен')
    await expectOpenSuggestionList(page, address, 'Подсказки адреса')
    await address.press('ArrowDown')
    await address.press('Enter')

    await expectClosedSuggestionList(page, address, 'Подсказки адреса')
    await expect(address).toHaveValue('ул. Ленина, 1')
    await expect(dialog.getByLabel('Почтовый индекс')).toHaveValue('620000')
    await expect(dialog.getByLabel('Широта')).toHaveValue('56.8389')
    await expect(dialog.getByLabel('Долгота')).toHaveValue('60.5975')
    await expect(dialog.getByLabel('Часовой пояс')).toHaveValue('Europe/Moscow')

    // Enter выбирал подсказку, а не отправлял форму: записи нет.
    expect(writes).toEqual([])
  })

  test('попап не обрезается диалогом и скроллится внутри себя', async ({
    page,
  }) => {
    const manyCities: CitySuggestionMock[] = Array.from(
      { length: 12 },
      (_, index) => ({
        providerId: `e2e-city-${index + 1}`,
        display: `г. Город ${index + 1}`,
        city: `Город ${index + 1}`,
        region: 'E2E обл.',
        country: 'Россия',
        latitude: 56 + index / 100,
        longitude: 60 + index / 100,
      })
    )

    await mockAddressSuggestions(page, { cities: manyCities })

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')

    await city.fill('Гор')

    const listbox = await expectOpenSuggestionList(
      page,
      city,
      'Подсказки города'
    )
    // Popup — портал на уровне диалога, поэтому `overflow-hidden` формы и
    // прокручиваемого `FieldGroup` его не обрезают.
    const popup = listbox.locator('xpath=..')

    expect(
      await listbox.evaluate((element) =>
        element.closest('[role="dialog"]') === null
      )
    ).toBe(true)
    expect(
      await listbox.evaluate((element) =>
        element.closest('[data-slot="field-group"]') === null
      )
    ).toBe(true)

    await expectInsideViewport(page, popup)

    const popupBox = await popup.boundingBox()

    expect(popupBox).not.toBeNull()

    if (popupBox !== null) {
      // `max-h-64` (256 px): высокий список прокручивается внутри попапа.
      expect(popupBox.height).toBeLessThanOrEqual(260)
    }

    expect(
      await popup.evaluate((element) => element.scrollHeight)
    ).toBeGreaterThan(await popup.evaluate((element) => element.clientHeight))

    for (let index = 0; index < manyCities.length; index += 1) {
      await city.press('ArrowDown')
    }

    await expectActiveSuggestion(page, city, 'г. Город 12')
    expect(await popup.evaluate((element) => element.scrollTop)).toBeGreaterThan(
      0
    )

    // Точка в центре видимой части попапа принадлежит ему, а не подложке:
    // значит, он не спрятан за `overflow` и не перекрыт диалогом.
    const popupIsHit = await page.evaluate(() => {
      const popupElement =
        document.querySelector('[role="listbox"]')?.parentElement

      if (popupElement === null || popupElement === undefined) {
        return false
      }

      const rect = popupElement.getBoundingClientRect()
      const hit = document.elementFromPoint(
        rect.left + rect.width / 2,
        rect.top + rect.height / 2
      )

      return hit !== null && popupElement.contains(hit)
    })

    expect(popupIsHit).toBe(true)
    await expectNoHorizontalScroll(page)
  })

  test('пустой ответ и загрузка объявляются через role="status"', async ({
    page,
  }) => {
    let release: () => void = () => {}
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })

    await page.route('**/api/addresses/suggest*', (route) =>
      route.fulfill({ json: SUGGESTION_ADDRESSES })
    )
    await page.route('**/api/addresses/cities*', async (route) => {
      const query = new URL(route.request().url()).searchParams.get('query')

      if (query === 'Хан') {
        await route.fulfill({ json: [] })
        return
      }

      // Держим ответ, чтобы состояние загрузки было наблюдаемым без sleep.
      await gate
      await route.fulfill({ json: SUGGESTION_CITIES })
    })

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')

    await city.fill('Хан')

    const empty = page.getByText('Ничего не найдено.')

    await expect(empty).toBeVisible()
    await expect(empty).toHaveAttribute('role', 'status')
    await expect(city).toHaveAttribute('aria-expanded', 'true')
    // Пустой ответ не рисует пунктов: список остаётся пустым, а текст живёт
    // в `role="status"` рядом с ним.
    await expect(
      page
        .getByRole('listbox', { name: 'Подсказки города' })
        .getByRole('option')
    ).toHaveCount(0)

    await city.fill('')
    await city.fill('Екат')

    const loading = page.getByText('Загрузка подсказок…')

    await expect(loading).toBeVisible()
    await expect(loading).toHaveAttribute('role', 'status')
    await expect(loading).toHaveClass(/sr-only/)

    release()
    await expect(
      page.getByRole('option', { name: 'г. Екатеринбург', exact: true })
    ).toBeVisible()
    await expect(loading).toBeHidden()
  })
})

test.describe('подсказки салона на 320 px', () => {
  test.use({
    storageState: storageStatePath('admin'),
    viewport: { width: 320, height: 800 },
  })

  test('списки остаются во вьюпорте и не дают горизонтального скролла', async ({
    page,
  }) => {
    await mockAddressSuggestions(page)

    const dialog = await openSalonEditDialog(page)
    const city = dialog.getByLabel('Город')

    await city.fill('Екат')

    const cityList = await expectOpenSuggestionList(
      page,
      city,
      'Подсказки города'
    )

    await expectInsideViewport(page, cityList.locator('xpath=..'))
    await expectNoHorizontalScroll(page)

    // Выбор города делает подсказки адреса доступными.
    await city.press('ArrowDown')
    await city.press('Enter')
    await expectClosedSuggestionList(page, city, 'Подсказки города')

    const address = dialog.getByLabel('Адрес', { exact: true })

    await address.fill('ул. Лен')

    const addressList = await expectOpenSuggestionList(
      page,
      address,
      'Подсказки адреса'
    )

    await expectInsideViewport(page, addressList.locator('xpath=..'))
    await expectNoHorizontalScroll(page)
    expect(
      await addressList.evaluate((element) =>
        element.closest('[role="dialog"]') === null
      )
    ).toBe(true)
  })
})

test.describe('подсказки в кабинете барбера', () => {
  test.use({
    storageState: storageStatePath('barber'),
    viewport: { width: 1280, height: 900 },
  })

  test('создание салона: подсказки города и адреса', async ({ page }) => {
    await mockAddressSuggestions(page)

    const dialog = await openOwnerSalonDialog(page)
    const city = dialog.getByLabel('Город')
    const address = dialog.getByLabel('Адрес', { exact: true })
    const cityHint = dialog.getByText('Сначала выберите город.')

    // Пока город пуст, адресные подсказки выключены и форма это объясняет.
    await expect(cityHint).toBeVisible()

    await city.fill('Екат')

    const listbox = await expectOpenSuggestionList(
      page,
      city,
      'Подсказки города'
    )

    await expect(cityHint).toBeHidden()
    await city.press('Escape')
    await expectClosedSuggestionList(page, city, 'Подсказки города')
    await expect(city).toHaveValue('Екат')

    // Повторное открытие стрелкой и выбор второго города с клавиатуры.
    await city.press('ArrowDown')
    await expect(listbox).toBeVisible()
    await city.press('ArrowDown')
    await city.press('Enter')

    await expect(city).toHaveValue('Екатеринбург')
    await expect(cityHint).toBeHidden()

    await address.fill('ул. Лен')
    await expectOpenSuggestionList(page, address, 'Подсказки адреса')
    await address.press('ArrowDown')
    await address.press('Enter')

    await expect(address).toHaveValue('ул. Ленина, 1')
    // Владелец при создании не видит индекс: скрытое поле подсказка не заводит.
    await expect(dialog.getByLabel('Почтовый индекс')).toHaveCount(0)
    await expect(dialog.getByLabel('Широта')).toHaveValue('56.8389')
    await expect(dialog.getByLabel('Долгота')).toHaveValue('60.5975')
  })
})
