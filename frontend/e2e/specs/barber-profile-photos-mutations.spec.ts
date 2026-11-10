import { fileURLToPath } from 'node:url'

import { expect, test } from '../fixtures/test'

import type {
  APIRequestContext,
  BrowserContext,
  Page,
} from '@playwright/test'

import { baseURL, fixtures, storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { BarberProfilePage } from '../pages/barber-profile-page'

/**
 * Портфолио барбера: загрузка, публичная галерея и блокировка админом (§6.4).
 *
 * Сценарий идёт по изолированному `profileBarber`: витринного барбера (`…0001`)
 * не трогаем, на его карточке, бронях и отзывах держатся другие спеки. Имя файла
 * содержит `mutations`, поэтому проект `mutations-chromium` запускает его
 * последним; `serial` обязателен — каждая сцена продолжает предыдущую: загруженное
 * фото открывается публично, скрывается админом, возвращается и наконец удаляется.
 *
 * Файл `e2e/fixtures/portfolio-photo.png` — настоящий PNG 800×600, а не 1×1:
 * только так `naturalWidth > 0` доказывает, что реальный файл прошёл
 * multipart-загрузку, клиентскую подготовку и раздачу `/api/media/portfolio/...`.
 *
 * Если сцена упала, `afterEach` удаляет оставшееся фото через API от имени
 * барбера: строки не должны доживать до следующего прогона. Успешные сцены
 * передают фото дальше и ничего не убирают — иначе сцене удаления нечего было бы
 * удалять.
 */
const PHOTO_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const CAPTION = 'E2E Работа'
const PROFILE_ID = fixtures.profileBarber.profileId
const PROFILE_NAME = 'E2E Барбер Кабинета'

const PORTFOLIO_SECTION = 'section[aria-labelledby="barber-portfolio-heading"]'
const ADMIN_PHOTOS_SECTION = 'section[aria-labelledby="barber-photos-title"]'

type OwnCard = {
  photos: { id: string }[]
}

type PublicCard = {
  photos: { id: string; description: string | null }[]
}

test.use({ storageState: storageStatePath('profileBarber') })

test.describe.configure({ mode: 'serial' })

test.describe('фотографии портфолио: мутации', () => {
  test.afterEach(async ({ request }, testInfo) => {
    // Упавшая сцена могла оставить фото: убираем его API-вызовом барбера.
    // Успешная сцена передаёт фото следующей, поэтому здесь ничего не трогаем.
    if (testInfo.status !== testInfo.expectedStatus) {
      await deletePortfolioPhotos(request)
    }
  })

  test.afterAll(async ({ playwright }) => {
    // Последняя страховка после падения последней сцены: контекст запросов
    // с тем же storageState, что и у браузера. Ошибку чтения не раздуваем —
    // afterEach уже пытался убрать строки.
    const context = await playwright.request.newContext({
      baseURL,
      storageState: storageStatePath('profileBarber'),
    })

    try {
      await deletePortfolioPhotos(context, { strict: false })
    } finally {
      await context.dispose()
    }
  })

  test('барбер загружает фото и видит настоящий файл', async ({ page }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()
    await profile.openPortfolio()

    // Сид держит портфолио `profileBarber` пустым: сцена начинается с нуля.
    await expect(profile.emptyPhotos).toBeVisible()
    await expect(profile.photoCards()).toHaveCount(0)

    await profile.uploadPhoto(PHOTO_PATH, CAPTION)

    // Объявление приходит после `201`, когда карточка уже перечитывается.
    await expect(profile.announcement('Фото добавлено.')).toBeVisible()
    await expect(profile.uploadDialog).toBeHidden()
    await expect(profile.photoCards()).toHaveCount(1)

    const card = profile.photoCards().first()

    await expect(card).toContainText(CAPTION)

    const image = card.getByRole('img')

    await expect(image).toBeVisible()
    await expect(image).toHaveAttribute('alt', CAPTION)
    // `naturalWidth > 0` — доказательство, что API отдал настоящий PNG по
    // относительному `/api/media/portfolio/...`, а не alt-подложку.
    await expect
      .poll(
        () =>
          image.evaluate(
            (node) => (node as HTMLImageElement).naturalWidth
          ),
        { message: 'файл портфолио отдан API, а не только строка в ответе' }
      )
      .toBeGreaterThan(0)
  })

  test('публичная галерея: лайтбокс, края, Escape и возврат фокуса', async ({
    page,
  }) => {
    await page.goto(`/barbers/${PROFILE_ID}`)

    await expect(
      page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
    ).toBeVisible()

    const gallery = page.locator(PORTFOLIO_SECTION)

    await expect(
      gallery.getByRole('heading', { name: 'Работы', level: 2 })
    ).toBeVisible()

    const tiles = gallery.getByRole('button', {
      name: `Открыть фото: ${CAPTION}`,
    })

    await expect(tiles).toHaveCount(1)

    // axe на 1280: галерея в её реальном состоянии, без исключений.
    await expectNoBlockingViolations(
      page,
      `/barbers/{id} + «Работы» @1280`,
      test.info()
    )

    await tiles.first().click()

    const lightbox = page.getByRole('dialog', { name: 'Фото барбера' })

    await expect(lightbox).toBeVisible()
    await expect(lightbox.getByRole('img', { name: CAPTION })).toBeVisible()
    await expect(lightbox.getByText('1 из 1', { exact: true })).toBeVisible()

    // Одно фото: обе стрелки на краях выключены, но остаются в разметке.
    const previous = lightbox.getByRole('button', { name: /^Назад/ })
    const next = lightbox.getByRole('button', { name: /^Дальше/ })

    await expect(previous).toBeDisabled()
    await expect(next).toBeDisabled()

    // Стрелки на краях не меняют индекс: счётчик остаётся прежним.
    await page.keyboard.press('ArrowRight')
    await page.keyboard.press('ArrowLeft')
    await expect(lightbox.getByText('1 из 1', { exact: true })).toBeVisible()

    // Открытый лайтбокс — отдельный слой: тот же axe-гейт, без allow-list.
    await expectNoBlockingViolations(
      page,
      `лайтбокс «Фото барбера» @1280`,
      test.info()
    )

    await page.keyboard.press('Escape')

    await expect(lightbox).toBeHidden()
    // Base UI возвращает фокус на кнопку-триггер, а не в `<body>`.
    await expect(tiles.first()).toBeFocused()
  })

  test('320 px: без горизонтального скролла, axe и лайтбокс', async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 800 })
    await page.goto(`/barbers/${PROFILE_ID}`)

    const gallery = page.locator(PORTFOLIO_SECTION)

    await expect(
      gallery.getByRole('heading', { name: 'Работы', level: 2 })
    ).toBeVisible()

    // Сетка 2 колонки и подписи не расширяют страницу.
    const scrollWidth = await page.evaluate(
      () => document.documentElement.scrollWidth
    )

    expect(scrollWidth).toBeLessThanOrEqual(320)

    await expectNoBlockingViolations(
      page,
      `/barbers/{id} + «Работы» @320`,
      test.info()
    )

    const tile = gallery.getByRole('button', {
      name: `Открыть фото: ${CAPTION}`,
    })

    await tile.click()

    const lightbox = page.getByRole('dialog', { name: 'Фото барбера' })

    await expect(lightbox).toBeVisible()
    await expect(lightbox.getByRole('img', { name: CAPTION })).toBeVisible()

    // Лайтбокс на 320 не шире экрана.
    const box = await lightbox.boundingBox()

    expect(box?.width ?? 0).toBeLessThanOrEqual(320)

    await page.keyboard.press('Escape')
    await expect(lightbox).toBeHidden()
    await expect(tile).toBeFocused()
  })

  test('админ скрывает фото, публичная карточка теряет галерею, затем возвращает', async ({
    browser,
  }) => {
    const adminContext = await browser.newContext({
      baseURL,
      storageState: storageStatePath('admin'),
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })
    const publicContext = await browser.newContext({
      baseURL,
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/barbers/${PROFILE_ID}`)

      // Карточка грузится по id напрямую: фильтр списка на неё не влияет.
      await expect(adminPage.locator('#barber-detail')).toBeVisible()

      const photos = adminPage.locator(ADMIN_PHOTOS_SECTION)

      await expect(
        photos.getByRole('heading', { name: 'Фотографии', level: 3 })
      ).toBeVisible()
      await expect(photos.getByText(CAPTION, { exact: true })).toBeVisible()
      await expect(
        photos.getByText('Опубликовано', { exact: true })
      ).toBeVisible()

      await photos
        .getByRole('button', { name: `Скрыть: ${CAPTION}` })
        .click()

      const confirm = adminPage.getByRole('alertdialog', {
        name: 'Скрыть фото?',
      })

      await expect(confirm).toBeVisible()
      await expect(confirm).toContainText(
        'Фото исчезнет с публичной карточки. Барбер не сможет вернуть его сам.'
      )

      await confirm.getByRole('button', { name: 'Скрыть', exact: true }).click()

      await expect(
        photos.getByText('Скрыто администратором', { exact: true })
      ).toBeVisible()
      await expect(
        adminPage
          .getByRole('status')
          .filter({ hasText: 'Фото скрыто: его больше нет на публичной карточке.' })
      ).toBeVisible()

      const publicPage = await publicContext.newPage()

      await expectPublicPortfolio(publicPage, { visible: false })
      expect((await fetchPublicPhotos(publicContext)).length).toBe(0)

      // «Разблокировать» — единственный путь вернуть кадр: владелец этого не может.
      await photos
        .getByRole('button', { name: `Разблокировать: ${CAPTION}` })
        .click()

      await expect(photos.getByText('Опубликовано', { exact: true })).toBeVisible()
      await expect(
        adminPage
          .getByRole('status')
          .filter({ hasText: 'Фото снова на публичной карточке.' })
      ).toBeVisible()

      await expectPublicPortfolio(publicPage, { visible: true })
      expect((await fetchPublicPhotos(publicContext)).length).toBe(1)
    } finally {
      await adminContext.close()
      await publicContext.close()
    }
  })

  test('барбер удаляет фото: пустое состояние и карточка без «Работы»', async ({
    browser,
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()
    await profile.openPortfolio()

    await expect(profile.photoCards()).toHaveCount(1)

    await profile.deletePhoto(0)

    await expect(profile.deleteDialog).toBeVisible()
    await expect(profile.deleteDialog).toContainText(
      'Фото исчезнет из карточки и поиска. Это необратимо.'
    )

    await profile.confirmDelete()

    await expect(profile.announcement('Фото удалено.')).toBeVisible()
    await expect(profile.deleteDialog).toBeHidden()
    await expect(profile.emptyPhotos).toBeVisible()
    await expect(profile.photoCards()).toHaveCount(0)

    const publicContext = await browser.newContext({
      baseURL,
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const publicPage = await publicContext.newPage()

      await expectPublicPortfolio(publicPage, { visible: false })
      expect((await fetchPublicPhotos(publicContext)).length).toBe(0)
    } finally {
      await publicContext.close()
    }
  })
})

/**
 * Публичная карточка в свежем контексте: галерея есть или её нет вовсе.
 *
 * Ждём загруженный профиль по `h1` (карточка не скелетон) и проверяем секцию
 * целиком: пустое портфолио — отсутствие «Работы», а не пустая карточка (§5.4).
 */
async function expectPublicPortfolio(
  page: Page,
  options: { visible: boolean }
): Promise<void> {
  await page.goto(`/barbers/${PROFILE_ID}`)
  await expect(
    page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
  ).toBeVisible()

  const gallery = page.locator(PORTFOLIO_SECTION)

  if (options.visible) {
    await expect(
      gallery.getByRole('heading', { name: 'Работы', level: 2 })
    ).toBeVisible()
    await expect(
      gallery.getByRole('button', { name: `Открыть фото: ${CAPTION}` })
    ).toBeVisible()
  } else {
    await expect(gallery).toHaveCount(0)
    await expect(
      page.getByRole('heading', { name: 'Работы', level: 2 })
    ).toHaveCount(0)
  }
}

/** Публичный ответ API: только видимые фото, без `isBlocked`. */
async function fetchPublicPhotos(context: BrowserContext): Promise<unknown[]> {
  const response = await context.request.get(`/api/barbers/${PROFILE_ID}`)

  expect(response.status(), 'GET /api/barbers/{id}').toBe(200)

  const body = (await response.json()) as PublicCard

  return body.photos
}

/** Удалить все фотографии `profileBarber` через API от имени владельца. */
async function deletePortfolioPhotos(
  request: APIRequestContext,
  options: { strict?: boolean } = {}
): Promise<void> {
  const strict = options.strict ?? true

  const own = await request.get(`/api/barber-profiles/${PROFILE_ID}`)

  if (own.status() !== 200) {
    if (strict) {
      expect(own.status(), 'GET /api/barber-profiles/{id}').toBe(200)
    }

    return
  }

  const { photos } = (await own.json()) as OwnCard

  if (photos.length === 0) {
    return
  }

  const csrf = await request.get('/api/auth/csrf')

  if (csrf.status() !== 200) {
    if (strict) {
      expect(csrf.status(), 'GET /api/auth/csrf').toBe(200)
    }

    return
  }

  const { token } = (await csrf.json()) as { token: string }

  for (const photo of photos) {
    const response = await request.delete(
      `/api/barber-profiles/${PROFILE_ID}/photos/${photo.id}`,
      { headers: { 'X-CSRF-TOKEN': token } }
    )

    // `404` терпим: параллельная уборка могла уже удалить строку.
    expect(
      [204, 404],
      `DELETE фото ${photo.id}: ${response.status()}`
    ).toContain(response.status())
  }
}
