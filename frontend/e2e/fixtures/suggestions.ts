import { expect, type Locator, type Page } from '@playwright/test'

/**
 * Подсказки города и адреса в E2E: ответы провайдера подменяются через `page.route`.
 *
 * Провайдер в тестовой среде выключен (`AddressSuggestions__Enabled=false`), поэтому
 * оба эндпоинта отвечают `409`, и без мока открыть список нельзя. Данные совпадают
 * по форме с ответом сервера и проходят `response-guards`
 * (`isCitySuggestionList` / `isAddressSuggestionList`): недостающее поле дало бы
 * честное «Ничего не найдено.» и сценарий проверял бы не то.
 *
 * Помощники `expectOpenSuggestionList` / `expectClosedSuggestionList` /
 * `expectActiveSuggestion` фиксируют ARIA-контракт combobox (§11): список имеет
 * `role="listbox"` и подпись, поле связано с ним `aria-controls`, подсветка пункта
 * объявляется через `aria-activedescendant`.
 */

export type CitySuggestionMock = {
  providerId: string | null
  display: string
  city: string
  region: string | null
  country: string
  latitude: number | null
  longitude: number | null
}

export type AddressSuggestionMock = {
  providerId: string | null
  display: string
  address: string | null
  city: string
  region: string | null
  country: string
  postalCode: string | null
  latitude: number | null
  longitude: number | null
  timeZoneId: string | null
}

export const SUGGESTION_CITIES: readonly CitySuggestionMock[] = [
  {
    providerId: 'e2e-city-ekb',
    display: 'г. Екатеринбург',
    city: 'Екатеринбург',
    region: 'Свердловская обл.',
    country: 'Россия',
    latitude: 56.8389,
    longitude: 60.5975,
  },
  {
    providerId: 'e2e-city-istok',
    display: 'г. Екатеринбург, пос. Исток',
    city: 'Екатеринбург',
    region: 'Свердловская обл.',
    country: 'Россия',
    latitude: 56.9,
    longitude: 60.7,
  },
  {
    providerId: 'e2e-city-kazan',
    display: 'г. Казань',
    city: 'Казань',
    region: 'Республика Татарстан',
    country: 'Россия',
    latitude: 55.7963,
    longitude: 49.1088,
  },
]

export const SUGGESTION_ADDRESSES: readonly AddressSuggestionMock[] = [
  {
    providerId: 'e2e-address-lenina',
    display: 'г. Екатеринбург, ул. Ленина, д. 1',
    address: 'ул. Ленина, 1',
    city: 'Екатеринбург',
    region: 'Свердловская обл.',
    country: 'Россия',
    postalCode: '620000',
    latitude: 56.8389,
    longitude: 60.5975,
    timeZoneId: null,
  },
]

/** Подменяет оба эндпоинта подсказок; `overrides` заменяет данные целиком. */
export async function mockAddressSuggestions(
  page: Page,
  overrides: {
    cities?: readonly CitySuggestionMock[]
    addresses?: readonly AddressSuggestionMock[]
  } = {}
): Promise<void> {
  await page.route('**/api/addresses/cities*', (route) =>
    route.fulfill({ json: overrides.cities ?? SUGGESTION_CITIES })
  )
  await page.route('**/api/addresses/suggest*', (route) =>
    route.fulfill({ json: overrides.addresses ?? SUGGESTION_ADDRESSES })
  )
}

/** Открытый список: роль, подпись и ARIA-связь поля со списком. */
export async function expectOpenSuggestionList(
  page: Page,
  input: Locator,
  label: string
): Promise<Locator> {
  const listbox = page.getByRole('listbox', { name: label })

  await expect(listbox).toBeVisible()

  const listId = await listbox.evaluate((element) => element.id)

  expect(listId, 'у списка должен быть id для aria-controls').not.toBe('')
  await expect(input).toHaveAttribute('role', 'combobox')
  await expect(input).toHaveAttribute('aria-expanded', 'true')
  await expect(input).toHaveAttribute('aria-controls', listId)

  return listbox
}

/** Закрытый список: поле больше не раскрыто и не ссылается на список. */
export async function expectClosedSuggestionList(
  page: Page,
  input: Locator,
  label: string
): Promise<void> {
  await expect(page.getByRole('listbox', { name: label })).toBeHidden()
  await expect(input).toHaveAttribute('aria-expanded', 'false')
  expect(await input.getAttribute('aria-controls')).toBeNull()
}

/** Подсвеченный пункт: `aria-activedescendant` указывает на существующую опцию. */
export async function expectActiveSuggestion(
  page: Page,
  input: Locator,
  label: string
): Promise<void> {
  const optionId = await input.getAttribute('aria-activedescendant')

  expect(optionId, 'подсветка пункта идёт через aria-activedescendant').not.toBeNull()

  if (optionId === null) {
    return
  }

  const option = page.locator(`[id="${optionId}"]`)

  await expect(option).toHaveAttribute('role', 'option')
  await expect(option).toHaveText(label)
}
