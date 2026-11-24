import type {
  APIRequestContext,
  Browser,
  BrowserContext,
} from '@playwright/test'

import { expect, test } from '../fixtures/test'

import {
  accounts,
  baseURL,
  fixtures,
  storageStatePath,
} from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AdminAuditPage } from '../pages/admin-audit-page'
import { BarberProfilePage } from '../pages/barber-profile-page'

/**
 * `/barber/profile` — мутации (§5.9).
 *
 * Сценарий идёт по одному изолированному барберу `profileBarber`: витринного
 * барбера (`…0001`) не трогаем, на его услуге и бронях держатся другие спеки.
 * `serial` обязателен: цена меняется у услуги, добавленной предыдущим тестом,
 * а скрытие и возврат — её же состояния.
 *
 * Специализации здесь не мутируем: публичный справочник фикстур пуст, а любая
 * активная строка сдвинула бы счётчики `admin-catalogue.spec.ts`. Они покрыты
 * unit-тестами страницы.
 *
 * Журнал (#96): правка цены пишет `barber_service.updated`, поэтому сцена
 * сравнивает UI-журнал и `GET /api/admin/audit` и всегда возвращает цену
 * фикстуры — следующие тесты скрывают и возвращают ту же услугу.
 */
const SERVICE_NAME = 'E2E Стрижка'
const FIXTURE_PRICE = '2000'
const NEW_PRICE = '2100'
const PRICE_ANNOUNCEMENT = 'Цена обновлена.'

test.use({ storageState: storageStatePath('profileBarber') })

test.describe.configure({ mode: 'serial' })

test.describe('профиль барбера: мутации', () => {
  test('правка «О себе» переживает перезагрузку', async ({ page }) => {
    const profile = new BarberProfilePage(page)
    const bio = `E2E-био ${Date.now()}`

    await profile.goto()
    await profile.waitForReady()

    await profile.bio.fill(bio)
    await expect(profile.saveProfile).toBeEnabled()
    await profile.saveProfile.click()

    await expect(profile.announcement('Профиль сохранён.')).toBeVisible()

    await page.reload()
    await profile.waitForReady()
    await expect(profile.bio).toHaveValue(bio)
  })

  test('услуга из «E2E Стрижка» появляется в прайсе и на публичной карточке', async ({
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await profile.addServiceButton.click()

    // Справочник ленивый: select появляется после первого запроса каталога.
    await expect(profile.addDialog).toBeVisible()
    await profile.serviceSelect.selectOption({ label: 'E2E Стрижка' })
    await expect(profile.addDuration).toHaveValue('30')

    await profile.addPrice.fill('1500')
    await profile.addDuration.fill('45')
    await profile.addSubmit.click()

    await expect(profile.announcement('Услуга добавлена.')).toBeVisible()
    await expect(profile.addDialog).toBeHidden()

    const row = profile.serviceRow('E2E Стрижка')

    await expect(row).toBeVisible()
    await expect(row).toContainText('45 мин')
    await expect(row).toContainText(/1\s*500/)
    await expect(row.getByRole('button', { name: 'Изменить цену' })).toBeVisible()

    // Публичная карточка читает тот же профиль через `GET /api/barbers/{id}`.
    await page.goto(`/barbers/${fixtures.profileBarber.profileId}`)

    const publicServices = page.locator(
      'section[aria-labelledby="barber-services-heading"]'
    )

    await expect(publicServices.getByText('E2E Стрижка')).toBeVisible()
    await expect(publicServices).toContainText(/1\s*500/)
    await expect(publicServices).toContainText('45 мин')
  })

  test('новая цена видна и в прайсе, и публично', async ({ page }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    const row = profile.serviceRow('E2E Стрижка')

    await row.getByRole('button', { name: 'Изменить цену' }).click()

    await expect(profile.priceDialog).toBeVisible()
    await expect(profile.priceInput).toHaveValue('1500')
    await expect(profile.priceDuration).toHaveValue('45')

    await profile.priceInput.fill('2000')
    await profile.priceSubmit.click()

    await expect(profile.announcement('Цена обновлена.')).toBeVisible()
    await expect(profile.priceDialog).toBeHidden()
    await expect(row).toContainText(/2\s*000/)
    await expect(row).not.toContainText(/1\s*500/)

    await page.goto(`/barbers/${fixtures.profileBarber.profileId}`)

    const publicServices = page.locator(
      'section[aria-labelledby="barber-services-heading"]'
    )

    await expect(publicServices).toContainText(/2\s*000/)
    await expect(publicServices).not.toContainText(/1\s*500/)
  })

  test('новая цена видна в журнале «Услуга барбера» с diff в рублях и авторством барбера', async ({
    browser,
    page,
  }) => {
    const profile = new BarberProfilePage(page)
    let adminContext: BrowserContext | null = null

    try {
      // 1. Барбер меняет цену тем же диалогом, что и предыдущая сцена: услуга
      // уже существует, поэтому диалог открывается с ценой 2 000 ₽.
      await profile.goto()
      await profile.waitForReady()

      const row = profile.serviceRow(SERVICE_NAME)

      await row.getByRole('button', { name: 'Изменить цену' }).click()
      await expect(profile.priceDialog).toBeVisible()
      await expect(profile.priceInput).toHaveValue(FIXTURE_PRICE)

      await profile.priceInput.fill(NEW_PRICE)
      await profile.priceSubmit.click()

      await expect(profile.announcement(PRICE_ANNOUNCEMENT)).toBeVisible()
      await expect(profile.priceDialog).toBeHidden()
      await expect(row).toContainText(/2\s*100/)

      // `entityId` будущей строки журнала — id услуги в прайсе этого профиля.
      // Строк `barber_service` за прогон несколько (`created` и прошлые `updated`),
      // поэтому нужную ищем по id, действию и значению, а не «единственную».
      const barberServiceId = await profileServiceId(page.request)

      // 2. Администратор в отдельном контексте: журнал закрыт `AdminOnly`, и cookie
      // барбера не должны смешиваться с админской сессией.
      adminContext = await openAdminContext(browser)

      const adminPage = await adminContext.newPage()
      const audit = new AdminAuditPage(adminPage)

      await audit.goto()
      await expect(audit.heading).toBeVisible()

      // Ждём ответ именно на выбранный тип, а не первый видимый ряд: до фильтра
      // на экране остаются строки «Все сущности».
      await Promise.all([
        adminPage.waitForResponse((response) => {
          const url = new URL(response.url())

          return (
            url.pathname === '/api/admin/audit' &&
            url.searchParams.get('entityType') === 'barber_service' &&
            response.status() === 200
          )
        }),
        audit.selectEntityType('Услуга барбера'),
      ])

      const firstRow = audit.rows.first()

      await expect(firstRow).toContainText('Услуга барбера')
      await expect(firstRow).toContainText('Изменено')
      await expect(firstRow).toContainText(accounts.profileBarber.email)

      await firstRow.getByRole('button', { name: 'Показать изменения' }).click()

      const diff = firstRow.locator('dl')

      await expect(diff).toBeVisible()
      // Цена — деньги, а не строка «5000»: «2 000 ₽ → 2 100 ₽». Пробелы в числе
      // неразрывные, поэтому матчим по цифрам, а не по тексту с NBSP.
      await expect(diff).toContainText('Цена')
      await expect(diff).toContainText(/2\s*000/)
      await expect(diff).toContainText(/2\s*100/)
      await expect(diff).toContainText(/2\s*000\s*₽\s*→\s*2\s*100\s*₽/)

      // 3. axe на 1280 с раскрытым diff'ом: тот же гейт, что в a11y-матрице,
      // без allow-list'а.
      await expectNoBlockingViolations(
        adminPage,
        '/admin/audit + diff «Услуга барбера» @1280',
        test.info()
      )

      // 4. Контракт API под администратором: та же строка, что и в интерфейсе.
      const entries = await fetchBarberServiceEntries(adminContext.request)
      const updated = entries.find(
        (entry) =>
          entry.entityId === barberServiceId &&
          entry.action === 'barber_service.updated' &&
          entry.changes.some(
            (change) => change.field === 'price' && change.to === 2100
          )
      )

      expect(
        updated,
        'GET /api/admin/audit?entityType=barber_service: строка с ценой 2100'
      ).toBeDefined()
      expect(updated?.entityType).toBe('barber_service')
      expect(updated?.actor?.email, 'автор строки — профильный барбер').toBe(
        accounts.profileBarber.email
      )
    } finally {
      // Спека `serial`: следующие сцены скрывают и возвращают ту же услугу,
      // поэтому цену возвращаем при любом исходе, даже если упало утверждение.
      // Контекст закрываем последним, чтобы он закрылся и при ошибке уборки.
      try {
        await restoreServicePrice(profile)
      } finally {
        await adminContext?.close()
      }
    }
  })

  test('«Скрыть» оставляет строку со статусом «Скрыта» и убирает её из публичной карточки', async ({
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    const row = profile.serviceRow('E2E Стрижка')

    await row.getByRole('button', { name: 'Скрыть', exact: true }).click()

    // В строке тоже есть «Скрыть» — подтверждение ищем по роли alertdialog.
    await expect(profile.hideDialog).toBeVisible()
    await expect(
      profile.hideDialog.getByText(
        'Услуга исчезнет из карточки и поиска. Записи, где она была, останутся в истории.'
      )
    ).toBeVisible()

    await profile.hideConfirm.click()

    await expect(profile.announcement('Услуга скрыта.')).toBeVisible()
    await expect(profile.hideDialog).toBeHidden()

    // Скрытая строка не исчезает из своего прайса: иначе вернуть её нечем.
    await expect(row).toBeVisible()
    await expect(row.getByText('Скрыта', { exact: true })).toBeVisible()
    await expect(row.getByRole('button', { name: 'Вернуть' })).toBeVisible()
    await expect(
      row.getByRole('button', { name: 'Изменить цену' })
    ).toHaveCount(0)

    await page.goto(`/barbers/${fixtures.profileBarber.profileId}`)

    const publicServices = page.locator(
      'section[aria-labelledby="barber-services-heading"]'
    )

    await expect(publicServices).toContainText('Услуги пока не добавлены.')
    await expect(publicServices.getByText('E2E Стрижка')).toHaveCount(0)
  })

  test('«Вернуть» снова делает услугу активной', async ({ page }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    const row = profile.serviceRow('E2E Стрижка')

    await expect(row.getByText('Скрыта', { exact: true })).toBeVisible()
    await row.getByRole('button', { name: 'Вернуть' }).click()

    await expect(profile.announcement('Услуга снова в прайсе.')).toBeVisible()
    await expect(row).toBeVisible()
    await expect(row.getByText('Скрыта', { exact: true })).toHaveCount(0)
    await expect(
      row.getByRole('button', { name: 'Изменить цену' })
    ).toBeVisible()
    await expect(row).toContainText(/2\s*000/)

    await page.goto(`/barbers/${fixtures.profileBarber.profileId}`)

    const publicServices = page.locator(
      'section[aria-labelledby="barber-services-heading"]'
    )

    await expect(publicServices.getByText('E2E Стрижка')).toBeVisible()
    await expect(publicServices).toContainText(/2\s*000/)
  })
})

/**
 * Отдельный админский контекст.
 *
 * Журнал закрыт `AdminOnly`, а барбер не должен делить cookie с администратором:
 * иначе сцена проверяла бы чтение журнала от лица владельца профиля.
 */
function openAdminContext(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({
    baseURL,
    storageState: storageStatePath('admin'),
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce',
  })
}

/** Изменение одного поля в строке журнала: JSON-скаляр или `null`. */
type AuditChange = {
  field: string
  from: string | number | boolean | null
  to: string | number | boolean | null
}

/** Строка журнала в форме, которую отдаёт `AdminAuditEntryResponse`. */
type AuditEntry = {
  action: string
  entityType: string
  entityId: string
  actor: { id: string; email: string | null } | null
  changes: AuditChange[]
}

/**
 * `entityId` строки журнала — id услуги в прайсе профиля.
 *
 * `GET /api/barber-profiles/{id}` отдаёт и скрытые услуги, поэтому строка
 * находится, пока предыдущий тест её не спрятал — здесь она ещё активна.
 */
async function profileServiceId(request: APIRequestContext): Promise<string> {
  const response = await request.get(
    `/api/barber-profiles/${fixtures.profileBarber.profileId}`
  )

  expect(response.status(), 'GET /api/barber-profiles/{id}').toBe(200)

  const body = (await response.json()) as {
    services: { barberServiceId: string; name: string }[]
  }
  const service = body.services.find((item) => item.name === SERVICE_NAME)

  expect(service, `в прайсе барбера есть «${SERVICE_NAME}»`).toBeDefined()

  return service?.barberServiceId ?? ''
}

/** Строки журнала типа `barber_service` — так же, как их запрашивает экран. */
async function fetchBarberServiceEntries(
  request: APIRequestContext
): Promise<AuditEntry[]> {
  const response = await request.get(
    '/api/admin/audit?entityType=barber_service&pageSize=100'
  )

  expect(
    response.status(),
    'GET /api/admin/audit?entityType=barber_service'
  ).toBe(200)

  const body = (await response.json()) as { items: AuditEntry[] }

  return body.items.filter((entry) => entry.entityType === 'barber_service')
}

/**
 * Возвращает цену услуги к фикстуре тем же диалогом, что и сцена.
 *
 * После падения проверка могла оставить открытый диалог, поэтому сначала
 * перезагружаем экран: он приходит с сервера и закрывает любое состояние.
 * Сохранение того же значения допустимо и идемпотентно — аудит не пишет строку
 * без фактических изменений.
 */
async function restoreServicePrice(profile: BarberProfilePage): Promise<void> {
  await profile.goto()
  await profile.waitForReady()

  const row = profile.serviceRow(SERVICE_NAME)

  await row.getByRole('button', { name: 'Изменить цену' }).click()
  await expect(profile.priceDialog).toBeVisible()
  await profile.priceInput.fill(FIXTURE_PRICE)
  await profile.priceSubmit.click()

  await expect(profile.announcement(PRICE_ANNOUNCEMENT)).toBeVisible()
  await expect(profile.priceDialog).toBeHidden()
  await expect(row).toContainText(/2\s*000/)
}
