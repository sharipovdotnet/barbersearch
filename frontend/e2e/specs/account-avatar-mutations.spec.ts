import { fileURLToPath } from 'node:url'

import { expect, test } from '../fixtures/test'

import {
  baseURL,
  fixtures,
  storageStatePath,
} from '../fixtures/accounts'
import { deleteMyAvatar } from '../fixtures/api'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AccountProfilePage } from '../pages/account-profile-page'

/**
 * Аватар пользователя (#85): загрузка, каркас, админская карточка и удаление.
 *
 * Сценарий идёт по мутирующему клиенту `mutations`: имя, отзывы и брони других
 * фикстур он не делит, а `seed.sql` сбрасывает его `avatar_url`. Витринного
 * барбера (`…0001`) не трогаем — на нём держатся поиск, карточка и брони.
 *
 * Файл `e2e/fixtures/portfolio-photo.png` — настоящий PNG 800×600: только так
 * `naturalWidth > 0` доказывает, что реальный файл прошёл клиентскую подготовку
 * (квадрат 512×512), multipart-загрузку и раздачу `/api/media/avatars/...`.
 *
 * Если сцена упала, `afterEach` удаляет аватар через API; успешные сцены передают
 * его следующей, иначе сцене удаления было бы нечего удалять.
 */
const AVATAR_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const INITIALS = 'МП'

test.use({ storageState: storageStatePath('mutations') })

test.describe.configure({ mode: 'serial' })

test.describe('аватар кабинета: мутации', () => {
  // `src` передаётся из сцены загрузки в админскую, 320-ю и сцену удаления.
  let avatarSrc: string | null = null

  test.afterEach(async ({ request }, testInfo) => {
    // Упавшая сцена могла оставить аватар: убираем его API-вызовом клиента.
    if (testInfo.status !== testInfo.expectedStatus) {
      await deleteMyAvatar(request)
    }
  })

  test('загрузка: настоящий файл, объявление, каркас и reload', async ({
    page,
  }) => {
    const profile = new AccountProfilePage(page)
    const headerImage = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
    )
    const headerFallback = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-fallback"]'
    )

    await profile.goto()
    await profile.waitForReady()

    // Сид держит аватар пустым: сцена начинается с инициалов.
    await expect(profile.avatarFallback).toContainText(INITIALS)
    await expect(profile.avatarImage).toHaveCount(0)
    // Шапка читает тот же профиль: до загрузки — инициалы, без отдельного запроса.
    await expect(headerFallback).toHaveAttribute('data-initials', INITIALS)
    await expect(headerImage).toHaveCount(0)

    await profile.uploadAvatar(AVATAR_PATH)

    await expect(
      profile.avatarStatus.filter({ hasText: 'Аватар обновлён.' })
    ).toBeVisible()
    await expect(profile.avatarImage).toBeVisible()

    const src = await profile.avatarImage.getAttribute('src')

    expect(src).toMatch(/^\/api\/media\/avatars\//)
    avatarSrc = src

    // Файл реально отдан API, а не подставлен строкой в разметку.
    await expect
      .poll(
        () =>
          profile.avatarImage.evaluate(
            (node) => (node as HTMLImageElement).naturalWidth
          ),
        { message: 'аватар отдан API, а не только строка в ответе' }
      )
      .toBeGreaterThan(0)

    // Каркас кабинета и шапка показывают тот же кадр без перезагрузки:
    // общий `ProfileProvider` обновил всех подписчиков через `setAvatar`.
    await expect(profile.identityAvatarImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('alt', '')

    // axe на 1280: карточка и каркас в реальном состоянии, без исключений.
    await expectNoBlockingViolations(
      page,
      '/account/profile + аватар @1280',
      test.info()
    )

    await page.reload()
    await profile.waitForReady()

    await expect(profile.avatarImage).toHaveAttribute('src', src ?? '')
    await expect(headerImage).toHaveAttribute('src', src ?? '')
  })

  test('админская карточка пользователя видит тот же src', async ({
    browser,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await browser.newContext({
      baseURL,
      storageState: storageStatePath('admin'),
      viewport: { width: 1280, height: 900 },
      reducedMotion: 'reduce',
    })

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/users/${fixtures.mutationsUserId}`)

      const detail = adminPage.locator('#user-detail')

      await expect(detail).toBeVisible()

      const image = detail.locator('[data-slot="avatar-image"]')

      await expect(image).toHaveAttribute('src', avatarSrc ?? '')
      await expect
        .poll(() =>
          image.evaluate((node) => (node as HTMLImageElement).naturalWidth)
        )
        .toBeGreaterThan(0)
    } finally {
      await adminContext.close()
    }
  })

  test('320 px: без горизонтального скролла, кнопки 44 px и axe', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 800 })
    await page.goto('/account/profile')

    const profile = new AccountProfilePage(page)

    await profile.waitForReady()
    await expect(profile.avatarImage).toBeVisible()

    const scrollWidth = await page.evaluate(
      () => document.documentElement.scrollWidth
    )

    expect(scrollWidth).toBeLessThanOrEqual(320)

    // Основное действие карточки — 44 px, а не десктопные 32.
    for (const button of [profile.uploadAvatarButton, profile.deleteAvatarButton]) {
      const box = await button.boundingBox()

      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)
    }

    await expectNoBlockingViolations(
      page,
      '/account/profile + аватар @320',
      test.info()
    )
  })

  test('удаление через alertdialog возвращает инициалы и отдаёт 404', async ({
    page,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const profile = new AccountProfilePage(page)
    const headerImage = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-image"]'
    )
    const headerFallback = page.locator(
      'button[aria-label="Меню аккаунта"] [data-slot="avatar-fallback"]'
    )

    await profile.goto()
    await profile.waitForReady()
    await expect(profile.avatarImage).toBeVisible()
    await expect(headerImage).toBeVisible()

    await profile.openAvatarDeleteDialog()

    await expect(profile.deleteAvatarDialog).toContainText(
      'Аватар исчезнет из карточки и кабинета. Файл удаляется безвозвратно.'
    )

    await profile.confirmAvatarDelete()

    await expect(
      profile.avatarStatus.filter({ hasText: 'Аватар удалён.' })
    ).toBeVisible()
    await expect(profile.deleteAvatarDialog).toBeHidden()
    await expect(profile.avatarImage).toHaveCount(0)
    await expect(profile.avatarFallback).toContainText(INITIALS)
    await expect(profile.identityAvatarImage).toHaveCount(0)
    // Шапка тоже сразу возвращается к инициалам, без перезагрузки.
    await expect(headerImage).toHaveCount(0)
    await expect(headerFallback).toHaveAttribute('data-initials', INITIALS)

    const response = await page.request.get(avatarSrc ?? '')

    expect(response.status(), 'GET старый /api/media/avatars/...').toBe(404)
  })
})
