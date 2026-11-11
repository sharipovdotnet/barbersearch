import { fileURLToPath } from 'node:url'

import type { Browser, BrowserContext, Locator, Page } from '@playwright/test'

import { expect, test } from '../fixtures/test'

import {
  baseURL,
  fixtures,
  storageStatePath,
} from '../fixtures/accounts'
import { deleteMyAvatar } from '../fixtures/api'
import { AccountProfilePage } from '../pages/account-profile-page'
import { AdminPhotosPage } from '../pages/admin-photos-page'
import { LandingPage } from '../pages/landing-page'
import { ProfilePage } from '../pages/profile-page'

/**
 * Публичный аватар барбера (#85, #89): кабинет → премодерация → `GET /api/barbers/{id}`
 * → герой и карточка поиска.
 *
 * Изолированный `profileBarber` подтверждён и активен, поэтому его карточка
 * доступна анониму. Витринного барбера (`…0001`) не трогаем: на нём держатся
 * поиск, отзывы и брони. `seed.sql` сбрасывает `avatar_url` и статус
 * премодерации этого профиля, так что второй прогон без пересева начинается
 * с инициалов.
 *
 * Премодерация (#89): после загрузки кадр ждёт проверки, поэтому публичные
 * контракты (`GET /api/barbers/{id}`, `GET /api/barbers?…`) отдают
 * `avatarUrl: null`, а герой и карточка поиска — инициалы. Публичным кадр
 * становится только после «Одобрить» в `/admin/photos?tab=avatars`. Сцены #87
 * (hide/unhide) и удаление идут после одобрения: блокировать имеет смысл только
 * одобренный кадр.
 *
 * Успешная загрузка передаёт `src` следующим сценам; `afterEach` при падении
 * убирает аватар API-вызовом и снимает флаг блокировки админским контекстом,
 * чтобы строка не дожила до следующего прогона.
 */
const AVATAR_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const PROFILE_ID = fixtures.profileBarber.profileId
const PROFILE_USER_ID = fixtures.profileBarber.userId
const PROFILE_NAME = 'E2E Барбер Кабинета'
// Email владельца профиля: по нему очередь аватаров ищет свою строку.
const PROFILE_EMAIL = 'e2e-profile-barber@barbersearch.test'
// Имя владельца в очереди — из аккаунта («Профиль Барберов»), а не displayName
// профиля: очередь показывает пользователя, а не карточку барбера.
const OWNER_NAME = 'Профиль Барберов'
const SEARCH_MARKER = 'E2E поиск'
// Кабинет считает инициалы по имени и фамилии аккаунта («Профиль Барберов»),
// а герой карточки — по displayName профиля («E2E Барбер Кабинета»): это разные
// источники, поэтому и константы разные. Первая буква героя — латинская `E`
// из «E2E», а не кириллическая «Е»: escape-последовательность не даёт
// юникод-омоглифу превратиться в невидимую опечатку.
const ACCOUNT_INITIALS = 'ПБ'
const HERO_INITIALS = 'E\u0411'
const HIDE_PHOTO = 'Скрыть фото'
const SHOW_PHOTO = 'Вернуть фото'

test.use({ storageState: storageStatePath('profileBarber') })

test.describe.configure({ mode: 'serial' })

test.describe('публичный аватар барбера: мутации', () => {
  let avatarSrc: string | null = null

  /** Карточка поиска по маркеру дополнительной услуги: она должна быть одна. */
  async function searchMarkerCard(page: Page): Promise<Locator> {
    const landing = new LandingPage(page)

    await landing.goto()
    await page.getByLabel('Дополнительная услуга').fill(SEARCH_MARKER)
    await landing.submit.click()

    const card = landing.cards.filter({ hasText: PROFILE_NAME })

    await expect(card).toHaveCount(1)

    return card
  }

  /** `avatarUrl` того же контракта поиска, что читает карточка. */
  async function searchMarkerAvatarUrl(page: Page): Promise<string | null> {
    const response = await page.request.get(
      `/api/barbers?additionalService=${encodeURIComponent(SEARCH_MARKER)}`
    )

    expect(response.status(), 'GET /api/barbers?additionalService').toBe(200)

    const body = (await response.json()) as {
      items: { displayName: string; avatarUrl: string | null }[]
    }

    expect(body.items).toHaveLength(1)
    expect(body.items[0]?.displayName).toBe(PROFILE_NAME)

    return body.items[0]?.avatarUrl ?? null
  }

  test.afterEach(async ({ browser, request }, testInfo) => {
    // Упавшая сцена могла оставить и аватар, и флаг блокировки: сначала снимаем
    // флаг админским контекстом, потом убираем файл API-вызовом барбера.
    if (testInfo.status !== testInfo.expectedStatus) {
      await unblockAvatarAsAdmin(browser, PROFILE_USER_ID)
      await deleteMyAvatar(request)
    }
  })

  test('загрузка ждёт проверки: публично `null`, герой и поиск — инициалы', async ({
    page,
  }) => {
    const account = new AccountProfilePage(page)

    await account.goto()
    await account.waitForReady()

    await expect(account.avatarFallback).toContainText(ACCOUNT_INITIALS)
    await expect(account.avatarImage).toHaveCount(0)

    await account.uploadAvatar(AVATAR_PATH)

    // Премодерация: успех — это «ждёт проверки», а не «обновлён».
    await expect(
      account.avatarStatus.filter({
        hasText: 'Аватар загружен и ждёт проверки.',
      })
    ).toBeVisible()
    await expect(account.avatarImage).toBeVisible()
    await expect(
      account.avatarSection.getByText('На проверке', { exact: true })
    ).toBeVisible()

    const src = await account.avatarImage.getAttribute('src')

    expect(src).toMatch(/^\/api\/media\/avatars\//)
    avatarSrc = src

    // Публичная карточка: контракт отдаёт `null`, герой — инициалы. Кадр
    // владельца при этом существует: его видит только владелец и админ.
    const publicProfile = new ProfilePage(page)

    await page.goto(`/barbers/${PROFILE_ID}`)
    await expect(
      page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
    ).toBeVisible()

    await expect(publicProfile.heroAvatarImage).toHaveCount(0)
    await expect(publicProfile.heroInitials).toContainText(HERO_INITIALS)

    const response = await page.request.get(`/api/barbers/${PROFILE_ID}`)

    expect(response.status(), 'GET /api/barbers/{id}').toBe(200)

    const body = (await response.json()) as { avatarUrl: string | null }

    expect(body.avatarUrl, 'публичный контракт не отдаёт неодобренный кадр').toBeNull()

    // Поиск (#86): маркер изолирует карточку, в ней — инициалы.
    const card = await searchMarkerCard(page)

    await expect(card.locator('img')).toHaveCount(0)
    await expect(card.locator('[data-slot="avatar-fallback"]')).toContainText(
      HERO_INITIALS
    )

    expect(await searchMarkerAvatarUrl(page)).toBeNull()
  })

  test('админ одобряет кадр: контракты и поверхности отдают URL', async ({
    browser,
    page,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()
      const photos = new AdminPhotosPage(adminPage)

      await photos.goto('avatars')
      await photos.searchFor(PROFILE_EMAIL)

      // Своя строка по email владельца: размер очереди не проверяем.
      const row = photos.row(PROFILE_EMAIL)

      await expect(row).toHaveCount(1)
      await expect(row).toContainText(OWNER_NAME)

      await photos.approve(row)

      await expect(photos.announcement('Фото одобрено.')).toBeVisible()
      await expect(row).toHaveCount(0)
    } finally {
      await adminContext.close()
    }

    // Одобрение — единственное, что делает кадр публичным: контракты отдают URL.
    const profileResponse = await page.request.get(`/api/barbers/${PROFILE_ID}`)

    expect(profileResponse.status(), 'GET /api/barbers/{id}').toBe(200)

    const profileBody = (await profileResponse.json()) as {
      avatarUrl: string | null
    }

    expect(profileBody.avatarUrl).toBe(avatarSrc)
    expect(await searchMarkerAvatarUrl(page)).toBe(avatarSrc)

    const publicProfile = new ProfilePage(page)

    await page.goto(`/barbers/${PROFILE_ID}`)
    await expect(
      page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
    ).toBeVisible()

    await expect(publicProfile.heroAvatarImage).toHaveAttribute(
      'src',
      avatarSrc ?? ''
    )
    await expect
      .poll(() =>
        publicProfile.heroAvatarImage.evaluate(
          (node) => (node as HTMLImageElement).naturalWidth
        )
      )
      .toBeGreaterThan(0)

    const card = await searchMarkerCard(page)
    const cardImage = card.locator('img')

    await expect(cardImage).toHaveAttribute('src', /^\/api\/media\/avatars\//)
    await expect(cardImage).toHaveAttribute('src', avatarSrc ?? '')
    await expect(cardImage).toHaveAttribute('alt', '')
    await expect
      .poll(() =>
        cardImage.evaluate((node) => (node as HTMLImageElement).naturalWidth)
      )
      .toBeGreaterThan(0)
  })

  test('админ скрывает фото: поиск и карточка — инициалы, контракт — null', async ({
    browser,
    page,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/barbers/${PROFILE_ID}`)

      const detail = adminPage.locator('#barber-detail')

      await expect(detail).toBeVisible()
      await expect(detail.locator('[data-slot="avatar-image"]')).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )

      // Кадр одобрен: блокировка доступна, статуса «На проверке» уже нет.
      await expect(
        detail.getByText('На проверке', { exact: true })
      ).toHaveCount(0)

      await detail.getByRole('button', { name: HIDE_PHOTO }).click()

      const confirm = adminPage.getByRole('alertdialog', {
        name: 'Скрыть фото?',
      })

      await expect(confirm).toBeVisible()
      await expect(confirm).toContainText(
        'Фото исчезнет из шапки, поиска, карточек и списков броней. Аккаунт останется активным, вернуть фото можно здесь же.'
      )

      await confirm
        .getByRole('button', { name: HIDE_PHOTO, exact: true })
        .click()

      await expect(
        detail.getByText('Фото скрыто', { exact: true })
      ).toBeVisible()
      await expect(
        detail.getByRole('button', { name: SHOW_PHOTO })
      ).toBeVisible()
      await expect(
        adminPage
          .getByRole('status')
          .filter({ hasText: 'Фото скрыто: его больше нет в публичных местах.' })
      ).toBeVisible()
      // Админ видит сырой кадр и при скрытом фото: иначе вернуть было бы нечего.
      await expect(detail.locator('[data-slot="avatar-image"]')).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )

      // Поиск: карточка остаётся в выдаче, но с инициалами; контракт — `null`.
      const card = await searchMarkerCard(page)

      await expect(card.locator('img')).toHaveCount(0)
      await expect(card.locator('[data-slot="avatar-fallback"]')).toContainText(
        HERO_INITIALS
      )

      expect(await searchMarkerAvatarUrl(page)).toBeNull()

      // Публичная карточка: тот же кадр, но инициалы; контракт — `null`.
      const publicProfile = new ProfilePage(page)

      await page.goto(`/barbers/${PROFILE_ID}`)
      await expect(
        page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
      ).toBeVisible()

      await expect(publicProfile.heroAvatarImage).toHaveCount(0)
      await expect(publicProfile.heroInitials).toContainText(HERO_INITIALS)

      const profileResponse = await page.request.get(`/api/barbers/${PROFILE_ID}`)

      expect(profileResponse.status(), 'GET /api/barbers/{id}').toBe(200)

      const profileBody = (await profileResponse.json()) as {
        avatarUrl: string | null
      }

      expect(profileBody.avatarUrl).toBeNull()

      // 320 px: строка `/admin/barbers` с аватаром 24 px и бейджем не должна
      // выдавливать имя и давать горизонтальный скролл (§15).
      await adminPage.setViewportSize({ width: 320, height: 800 })
      await adminPage.goto('/admin/barbers')
      await adminPage
        .getByRole('group', { name: 'Каких барберов показать' })
        .getByRole('button', { name: 'Все', exact: true })
        .click()
      await adminPage
        .getByLabel('Поиск по имени или email')
        .fill(PROFILE_NAME)

      const row = adminPage
        .locator('section[aria-label="Барберы"] li')
        .filter({ hasText: PROFILE_NAME })

      await expect(row).toHaveCount(1)
      await expect(
        row.getByText('Фото скрыто', { exact: true })
      ).toBeVisible()
      await expect(row.locator('[data-slot="avatar-image"]')).toBeVisible()

      const overflow = await adminPage.evaluate(
        () =>
          document.documentElement.scrollWidth -
          document.documentElement.clientWidth
      )

      expect(
        overflow,
        '/admin/barbers @320 со скрытым аватаром'
      ).toBeLessThanOrEqual(0)
    } finally {
      await adminContext.close()
    }
  })

  test('возврат фото: кадр, поиск и контракт снова видны', async ({
    browser,
    page,
  }) => {
    expect(avatarSrc).not.toBeNull()

    const adminContext = await openAdminContext(browser)

    try {
      const adminPage = await adminContext.newPage()

      await adminPage.goto(`/admin/barbers/${PROFILE_ID}`)

      const detail = adminPage.locator('#barber-detail')

      await expect(
        detail.getByText('Фото скрыто', { exact: true })
      ).toBeVisible()

      // Возврат обратим и подтверждения не требует — как разблокировка в портфолио.
      await detail.getByRole('button', { name: SHOW_PHOTO }).click()

      await expect(
        adminPage
          .getByRole('status')
          .filter({ hasText: 'Фото снова видно всем.' })
      ).toBeVisible()
      await expect(detail.getByText('Фото скрыто', { exact: true })).toHaveCount(
        0
      )
      await expect(
        detail.getByRole('button', { name: HIDE_PHOTO })
      ).toBeVisible()

      // Следующая сцена ждёт видимое фото в кабинете и в публичной карточке.
      const card = await searchMarkerCard(page)
      const cardImage = card.locator('img')

      await expect(cardImage).toHaveAttribute('src', avatarSrc ?? '')
      expect(await searchMarkerAvatarUrl(page)).toBe(avatarSrc)

      const publicProfile = new ProfilePage(page)

      await page.goto(`/barbers/${PROFILE_ID}`)
      await expect(
        page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
      ).toBeVisible()

      await expect(publicProfile.heroAvatarImage).toHaveAttribute(
        'src',
        avatarSrc ?? ''
      )
      await expect
        .poll(() =>
          publicProfile.heroAvatarImage.evaluate(
            (node) => (node as HTMLImageElement).naturalWidth
          )
        )
        .toBeGreaterThan(0)

      const profileResponse = await page.request.get(`/api/barbers/${PROFILE_ID}`)

      expect(profileResponse.status(), 'GET /api/barbers/{id}').toBe(200)

      const profileBody = (await profileResponse.json()) as {
        avatarUrl: string | null
      }

      expect(profileBody.avatarUrl).toBe(avatarSrc)
    } finally {
      await adminContext.close()
    }
  })

  test('удаление возвращает инициалы и `avatarUrl: null`', async ({ page }) => {
    expect(avatarSrc).not.toBeNull()

    const account = new AccountProfilePage(page)

    await account.goto()
    await account.waitForReady()
    await expect(account.avatarImage).toBeVisible()

    await account.openAvatarDeleteDialog()
    await account.confirmAvatarDelete()

    await expect(
      account.avatarStatus.filter({ hasText: 'Аватар удалён.' })
    ).toBeVisible()
    await expect(account.avatarFallback).toContainText(ACCOUNT_INITIALS)

    const response = await page.request.get(`/api/barbers/${PROFILE_ID}`)

    expect(response.status(), 'GET /api/barbers/{id}').toBe(200)

    const body = (await response.json()) as { avatarUrl: string | null }

    expect(body.avatarUrl).toBeNull()

    const publicProfile = new ProfilePage(page)

    await page.goto(`/barbers/${PROFILE_ID}`)
    await expect(
      page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
    ).toBeVisible()

    await expect(publicProfile.heroAvatarImage).toHaveCount(0)
    await expect(publicProfile.heroInitials).toContainText(HERO_INITIALS)

    // Поиск снова показывает инициалы, контракт — `avatarUrl: null`.
    const card = await searchMarkerCard(page)

    await expect(card.locator('img')).toHaveCount(0)
    await expect(card.locator('[data-slot="avatar-fallback"]')).toContainText(
      HERO_INITIALS
    )

    expect(await searchMarkerAvatarUrl(page)).toBeNull()
  })
})

/** Отдельный админский контекст: у `profileBarber` нет прав на модерацию фото. */
function openAdminContext(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({
    baseURL,
    storageState: storageStatePath('admin'),
    viewport: { width: 1280, height: 900 },
    reducedMotion: 'reduce',
  })
}

/** CSRF-токен контекста: cookie-мутация без него получает `400`. */
async function adminCsrfToken(page: Page): Promise<string | null> {
  const response = await page.request.get('/api/auth/csrf')

  if (response.status() !== 200) {
    return null
  }

  const body = (await response.json()) as { token: string }

  return body.token
}

/**
 * Уборка после упавшей сцены: снять флаг блокировки может только администратор.
 * Клиенту `profileBarber` эндпоинт недоступен. Повторный `DELETE` идемпотентен.
 */
async function unblockAvatarAsAdmin(
  browser: Browser,
  userId: string
): Promise<void> {
  const context = await browser.newContext({
    baseURL,
    storageState: storageStatePath('admin'),
  })

  try {
    const page = await context.newPage()
    const token = await adminCsrfToken(page)

    if (token === null) {
      return
    }

    const response = await page.request.delete(
      `/api/admin/users/${userId}/avatar/block`,
      { headers: { 'X-CSRF-TOKEN': token } }
    )

    expect(
      [204, 404],
      `DELETE api/admin/users/${userId}/avatar/block: ${response.status()}`
    ).toContain(response.status())
  } finally {
    await context.close()
  }
}
