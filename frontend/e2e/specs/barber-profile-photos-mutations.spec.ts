import { fileURLToPath } from 'node:url'

import { expect, test } from '../fixtures/test'

import type {
  APIRequestContext,
  BrowserContext,
  Page,
} from '@playwright/test'

import { baseURL, fixtures, storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AdminPhotosPage } from '../pages/admin-photos-page'
import { BarberProfilePage } from '../pages/barber-profile-page'

/**
 * Портфолио барбера: загрузка, премодерация и публичная галерея (§6.4, #89).
 *
 * Сценарий идёт по изолированному `profileBarber`: витринного барбера (`…0001`)
 * не трогаем, на его карточке, бронях и отзывах держатся другие спеки. Имя файла
 * содержит `mutations`, поэтому проект `mutations-chromium` запускает его
 * последним; `serial` обязателен — каждая сцена продолжает предыдущую.
 *
 * Файл `e2e/fixtures/portfolio-photo.png` — настоящий PNG 800×600, а не 1×1:
 * только так `naturalWidth > 0` доказывает, что реальный файл прошёл
 * multipart-загрузку, клиентскую подготовку и раздачу `/api/media/portfolio/...`.
 *
 * Премодерация (#89): загруженный кадр создаётся `Pending`, поэтому публичная
 * галерея и контракт `GET /api/barbers/{id}` пусты, а кабинет показывает бейдж
 * «На проверке». Публичным кадр делает «Одобрить» в `/admin/photos?tab=portfolio`;
 * «Отклонить» на втором кадре оставляет его непубличным и показывает владельцу
 * причину. Блокировка/разблокировка и удаление идут после одобрения.
 *
 * Если сцена упала, `afterEach` удаляет оставшиеся фото через API от имени
 * барбера: строки не должны доживать до следующего прогона.
 */
const PHOTO_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const CAPTION = 'E2E Работа'
const SECOND_CAPTION = 'E2E Работа 2'
const REJECTION_REASON = 'E2E: на кадре виден посторонний человек'
const PROFILE_ID = fixtures.profileBarber.profileId
const PROFILE_NAME = 'E2E Барбер Кабинета'

const PORTFOLIO_SECTION = 'section[aria-labelledby="barber-portfolio-heading"]'
const ADMIN_PHOTOS_SECTION = 'section[aria-labelledby="barber-photos-title"]'

// URL варианта `sm`, снятый с `srcset` карточки: проверяется в сценах одобрения,
// скрытия и возврата, когда кадр уже виден публично.
let portfolioVariantUrl: string | null = null

/** Первый URL из `srcset` (`?size=sm 256w, …`) — вариант, который грузит браузер. */
function firstSrcSetUrl(srcSet: string | null): string {
  expect(srcSet).not.toBeNull()
  expect(srcSet).toContain('?size=sm ')

  const url = srcSet?.split(',')[0]?.trim().split(' ')[0]

  expect(url).toMatch(
    /^\/api\/media\/portfolio\/[0-9a-f-]+\/[0-9a-f]{32}\.jpg\?size=sm$/
  )

  return url ?? ''
}

type OwnCard = {
  photos: {
    id: string
    description: string | null
    moderationStatus: string
    rejectionReason: string | null
  }[]
}

type PublicCard = {
  photos: {
    id: string
    url: string
    thumbnailUrl: string | null
    description: string | null
    moderationStatus: string
    rejectionReason: string | null
  }[]
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

  test('барбер загружает фото: «На проверке», публичной галереи нет', async ({
    browser,
    page,
  }) => {
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

    // Премодерация: кадр виден владельцу, но помечен как ждущий проверки.
    await expect(
      card.getByText('На проверке', { exact: true })
    ).toBeVisible()
    await expect(
      card.getByText('Кадр виден только вам, пока администратор его не одобрит.')
    ).toBeVisible()

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

    // Сетка запрашивает WebP-вариант через `srcset`, а не многомегабайтный
    // исходник: превью кабинета — та же раздача с `?size=`.
    const srcSet = await image.getAttribute('srcset')

    expect(srcSet).not.toBeNull()
    expect(srcSet, 'srcset карточки кабинета').toContain(
      '/api/media/portfolio/'
    )
    expect(srcSet).toContain('?size=sm 256w')
    expect(srcSet).toContain('?size=md 512w')
    expect(srcSet).toContain('?size=lg 1024w')

    portfolioVariantUrl = firstSrcSetUrl(srcSet)

    // Владелец видит свой `Pending`-кадр: вариант отдаётся как `image/webp`
    // с честным кэшем и ETag, повторный условный запрос — `304`.
    const variant = await page.request.get(portfolioVariantUrl)

    expect(variant.status(), 'GET варианта ?size=sm').toBe(200)
    expect(variant.headers()['content-type']).toBe('image/webp')

    const etag = variant.headers()['etag']

    expect(etag, 'ETag варианта').toBeTruthy()

    const revalidated = await page.request.get(portfolioVariantUrl, {
      headers: { 'If-None-Match': etag ?? '' },
    })

    expect(
      revalidated.status(),
      'If-None-Match с выданным ETag'
    ).toBe(304)
    expect(revalidated.headers()['etag']).toBe(etag)

    // Публично кадра ещё нет: ни галереи, ни фото в контракте. Гостевое чтение —
    // в отдельном контексте без cookie владельца.
    // Явно пустой storageState: `test.use` задаёт cookie владельца профиля, а
    // гостевая проверка обязана идти без них — иначе скрытый кадр отдаётся
    // владельцу с `private, no-store` и «скрытие» на самом деле не проверяется.
    const publicContext = await browser.newContext({
      baseURL,
      storageState: { cookies: [], origins: [] },
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

    // Владелец видит свой кадр в контракте собственной карточки — со статусом.
    const ownResponse = await page.request.get(
      `/api/barber-profiles/${PROFILE_ID}`
    )

    expect(ownResponse.status(), 'GET /api/barber-profiles/{id}').toBe(200)

    const own = (await ownResponse.json()) as OwnCard

    expect(own.photos).toHaveLength(1)
    expect(own.photos[0]?.moderationStatus).toBe('Pending')
    expect(own.photos[0]?.rejectionReason).toBeNull()
  })

  test('очередь портфолио: поиск по имени барбера и «Одобрить»', async ({
    browser,
  }) => {    const adminContext = await browser.newContext({
      baseURL,
      storageState: storageStatePath('admin'),
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const adminPage = await adminContext.newPage()
      const photos = new AdminPhotosPage(adminPage)

      await photos.goto('portfolio')
      await expect(photos.heading).toBeVisible()
      await expect(photos.tab('Портфолио')).toHaveAttribute(
        'aria-selected',
        'true'
      )

      // Своя строка — поиском по имени барбера; размер очереди не проверяем.
      await photos.searchFor(PROFILE_NAME)

      const row = photos.row(CAPTION)

      await expect(row).toHaveCount(1)
      await expect(row).toContainText(PROFILE_NAME)
      await expect(row).toContainText(CAPTION)

      // Строка портфолио с подписью — состояние, которого нет на пересеянной
      // базе: axe без allow-list проверяет его здесь (§11).
      await expectNoBlockingViolations(
        adminPage,
        '/admin/photos?tab=portfolio + строка @1280',
        test.info()
      )

      // Кнопка решения подписана именем барбера и подписью кадра; ищем её
      // внутри своей строки, чтобы не зависеть от полного `aria-label`.
      await photos.approve(row)

      await expect(photos.announcement('Фото одобрено.')).toBeVisible()
      await expect(row).toHaveCount(0)

      // Одобренный кадр сразу стал публичным: галерея и контракт.
      const publicContext = await browser.newContext({
        baseURL,
        viewport: { width: 1280, height: 900 },
        reducedMotion: 'reduce',
      })

      try {
        const publicPage = await publicContext.newPage()

        await expectPublicPortfolio(publicPage, { visible: true })

        const photos = await fetchPublicPhotos(publicContext)

        expect(photos).toHaveLength(1)
        expect(photos[0]?.description).toBe(CAPTION)
        // Публичный ответ отдаёт только `Approved` и без причины.
        expect(photos[0]?.moderationStatus).toBe('Approved')
        expect(photos[0]?.rejectionReason).toBeNull()
      } finally {
        await publicContext.close()
      }
    } finally {
      await adminContext.close()
    }
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

    // Публичная сетка тоже просит вариант: `srcset` с тремя ширинами и `sizes`
    // сетки 2/3/4 колонки.
    const galleryImage = gallery.getByRole('img', { name: CAPTION })

    await expect(galleryImage).toHaveAttribute(
      'srcset',
      /\/api\/media\/portfolio\/[^?]+\?size=sm 256w/
    )
    await expect(galleryImage).toHaveAttribute(
      'sizes',
      '(min-width: 1024px) 25vw, (min-width: 640px) 33vw, 50vw'
    )

    const originalSrc = await galleryImage.getAttribute('src')

    expect(originalSrc).toMatch(/^\/api\/media\/portfolio\/.+\.jpg$/)

    // axe на 1280: галерея в её реальном состоянии, без исключений.
    await expectNoBlockingViolations(
      page,
      `/barbers/{id} + «Работы» @1280`,
      test.info()
    )

    await tiles.first().click()

    const lightbox = page.getByRole('dialog', { name: 'Фото барбера' })
    const lightboxImage = lightbox.getByRole('img', { name: CAPTION })

    await expect(lightbox).toBeVisible()
    await expect(lightboxImage).toBeVisible()
    // Лайтбокс — исходник: качество важнее трафика, `srcset` здесь не нужен.
    await expect(lightboxImage).toHaveAttribute('src', originalSrc ?? '')
    await expect(lightboxImage).not.toHaveAttribute('srcset')
    await expect(lightboxImage).not.toHaveAttribute('sizes')
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

  test('второй кадр отклонён с причиной: владелец видит её, публично — только первый', async ({
    browser,
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()
    await profile.openPortfolio()

    await expect(profile.photoCards()).toHaveCount(1)

    // Второй кадр с другой подписью: он тоже `Pending` и тоже непубличен.
    await profile.uploadPhoto(PHOTO_PATH, SECOND_CAPTION)

    await expect(profile.announcement('Фото добавлено.')).toBeVisible()
    await expect(profile.photoCards()).toHaveCount(2)
    await expect(
      profile
        .photoCards()
        .nth(1)
        .getByText('На проверке', { exact: true })
    ).toBeVisible()

    const adminContext = await browser.newContext({
      baseURL,
      storageState: storageStatePath('admin'),
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const adminPage = await adminContext.newPage()
      const photos = new AdminPhotosPage(adminPage)

      await photos.goto('portfolio')
      await photos.searchFor(PROFILE_NAME)

      const row = photos.row(SECOND_CAPTION)

      await expect(row).toHaveCount(1)

      // Причина обязательна: диалог сначала показывается, потом заполняется.
      const dialog = await photos.openReject(row)

      await expect(dialog).toContainText(
        'Фото останется непубличным. Владелец увидит причину и сможет загрузить другое.'
      )

      await photos.rejectionReason.fill(REJECTION_REASON)
      await photos.rejectConfirm.click()

      await expect(photos.announcement('Фото отклонено.')).toBeVisible()
      await expect(row).toHaveCount(0)
    } finally {
      await adminContext.close()
    }

    // Владелец видит бейдж «Отклонено» и причину на втором кадре, первый цел.
    await profile.goto()
    await profile.waitForReady()
    await profile.openPortfolio()

    await expect(profile.photoCards()).toHaveCount(2)

    const rejected = profile.photoCards().nth(1)

    await expect(
      rejected.getByText('Отклонено', { exact: true })
    ).toBeVisible()
    await expect(
      rejected.getByText(`Причина: ${REJECTION_REASON}`)
    ).toBeVisible()

    // Публично отклонённого кадра нет: галерея по-прежнему одна.
    // Явно пустой storageState: `test.use` задаёт cookie владельца профиля, а
    // гостевая проверка обязана идти без них — иначе скрытый кадр отдаётся
    // владельцу с `private, no-store` и «скрытие» на самом деле не проверяется.
    const publicContext = await browser.newContext({
      baseURL,
      storageState: { cookies: [], origins: [] },
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const publicPage = await publicContext.newPage()

      await expectPublicPortfolio(publicPage, { visible: true })

      const publicPhotos = await fetchPublicPhotos(publicContext)

      expect(publicPhotos).toHaveLength(1)
      expect(publicPhotos[0]?.description).toBe(CAPTION)
    } finally {
      await publicContext.close()
    }

    // Собственный контракт отдаёт реальный статус и причину второго кадра.
    const ownResponse = await page.request.get(
      `/api/barber-profiles/${PROFILE_ID}`
    )

    expect(ownResponse.status(), 'GET /api/barber-profiles/{id}').toBe(200)

    const own = (await ownResponse.json()) as OwnCard

    const ownRejected = own.photos.find(
      (photo) => photo.description === SECOND_CAPTION
    )

    expect(ownRejected?.moderationStatus).toBe('Rejected')
    expect(ownRejected?.rejectionReason).toBe(REJECTION_REASON)
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
    // Явно пустой storageState: `test.use` задаёт cookie владельца профиля, а
    // гостевая проверка обязана идти без них — иначе скрытый кадр отдаётся
    // владельцу с `private, no-store` и «скрытие» на самом деле не проверяется.
    const publicContext = await browser.newContext({
      baseURL,
      storageState: { cookies: [], origins: [] },
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

      // Одобренный кадр показывает «Опубликовано», отклонённый — свою причину.
      const approved = photos
        .getByRole('listitem')
        .filter({ hasText: CAPTION })
        .first()

      await expect(approved).toContainText('Опубликовано')
      await expect(
        photos.getByText(`Причина: ${REJECTION_REASON}`)
      ).toBeVisible()

      // Адрес варианта берём из публичного контракта: `thumbnailUrl` — это
      // `{url}?size=md`, поэтому проверка привязана к самому кадру, а не к
      // порядку картинок в сетке.
      const visiblePhotos = await fetchPublicPhotos(publicContext)
      const visiblePhoto = visiblePhotos.find(
        (photo) => photo.description === CAPTION
      )

      expect(visiblePhoto, 'кадр в публичной карточке').toBeDefined()

      const hiddenVariantUrl = visiblePhoto?.thumbnailUrl ?? ''

      expect(hiddenVariantUrl, 'thumbnailUrl публичного кадра').toMatch(
        /^\/api\/media\/portfolio\/[0-9a-f-]+\/[0-9a-f]{32}\.[a-z]+\?size=md$/
      )

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

      // Скрытый кадр отвечает `404` и на `?size=`-URL: после перезагрузки
      // браузер не должен показать его из кэша.
      const hiddenVariant = await publicContext.request.get(hiddenVariantUrl)
      const hiddenStatus = hiddenVariant.status()

      expect(
        hiddenStatus,
        `GET ${hiddenVariantUrl} → ${hiddenStatus} ${JSON.stringify(
          hiddenVariant.headers()
        )}`
      ).toBe(404)
      expect(hiddenVariant.headers()['cache-control']).toBe('no-store')

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

      // Возврат публикует и вариант: он снова `200` и перепроверяется, а не
      // живёт сутки в кэше.
      const returnedVariant = await publicContext.request.get(hiddenVariantUrl)

      expect(returnedVariant.status(), 'GET ?size=-варианта после возврата').toBe(
        200
      )
      expect(returnedVariant.headers()['content-type']).toBe('image/webp')
      expect(returnedVariant.headers()['cache-control']).toBe(
        'public, max-age=0, must-revalidate'
      )
    } finally {
      await adminContext.close()
      await publicContext.close()
    }
  })

  test('барбер удаляет оба фото: пустое состояние и карточка без «Работы»', async ({
    browser,
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()
    await profile.openPortfolio()

    // На этом шаге в портфолио два кадра: одобренный и отклонённый.
    await expect(profile.photoCards()).toHaveCount(2)

    await profile.deletePhoto(0)

    await expect(profile.deleteDialog).toBeVisible()
    await expect(profile.deleteDialog).toContainText(
      'Фото исчезнет из карточки и поиска. Это необратимо.'
    )

    await profile.confirmDelete()

    await expect(profile.announcement('Фото удалено.')).toBeVisible()
    await expect(profile.deleteDialog).toBeHidden()
    await expect(profile.photoCards()).toHaveCount(1)

    // Отклонённый кадр удаляется тем же путём: удаление доступно на любом статусе.
    await profile.deletePhoto(0)
    await profile.confirmDelete()

    await expect(profile.deleteDialog).toBeHidden()
    await expect(profile.emptyPhotos).toBeVisible()
    await expect(profile.photoCards()).toHaveCount(0)

    // Явно пустой storageState: `test.use` задаёт cookie владельца профиля, а
    // гостевая проверка обязана идти без них — иначе скрытый кадр отдаётся
    // владельцу с `private, no-store` и «скрытие» на самом деле не проверяется.
    const publicContext = await browser.newContext({
      baseURL,
      storageState: { cookies: [], origins: [] },
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
async function fetchPublicPhotos(context: BrowserContext): Promise<PublicCard['photos']> {
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
