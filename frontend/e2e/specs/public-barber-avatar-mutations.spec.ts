import { fileURLToPath } from 'node:url'

import type { Locator, Page } from '@playwright/test'

import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { deleteMyAvatar } from '../fixtures/api'
import { AccountProfilePage } from '../pages/account-profile-page'
import { LandingPage } from '../pages/landing-page'
import { ProfilePage } from '../pages/profile-page'

/**
 * Публичный аватар барбера (#85): кабинет → `GET /api/barbers/{id}` → герой.
 *
 * Изолированный `profileBarber` подтверждён и активен, поэтому его карточка
 * доступна анониму. Витринного барбера (`…0001`) не трогаем: на нём держатся
 * поиск, отзывы и брони. `seed.sql` сбрасывает `avatar_url` этого профиля, так что
 * второй прогон без пересева начинается с инициалов.
 *
 * Успешная загрузка передаёт `src` сцене удаления; `afterEach` при падении убирает
 * аватар API-вызовом, чтобы строка не дожила до следующего прогона.
 */
const AVATAR_PATH = fileURLToPath(
  new URL('../fixtures/portfolio-photo.png', import.meta.url)
)
const PROFILE_ID = fixtures.profileBarber.profileId
const PROFILE_NAME = 'E2E Барбер Кабинета'
const SEARCH_MARKER = 'E2E поиск'
// Кабинет считает инициалы по имени и фамилии аккаунта («Профиль Барберов»),
// а герой карточки — по displayName профиля («E2E Барбер Кабинета»): это разные
// источники, поэтому и константы разные.
const ACCOUNT_INITIALS = 'ПБ'
const HERO_INITIALS = 'EБ'

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

  test.afterEach(async ({ request }, testInfo) => {
    if (testInfo.status !== testInfo.expectedStatus) {
      await deleteMyAvatar(request)
    }
  })

  test('загрузка видна в контракте карточки и в герое', async ({ page }) => {
    const account = new AccountProfilePage(page)

    await account.goto()
    await account.waitForReady()

    await expect(account.avatarFallback).toContainText(ACCOUNT_INITIALS)
    await expect(account.avatarImage).toHaveCount(0)

    await account.uploadAvatar(AVATAR_PATH)

    await expect(
      account.avatarStatus.filter({ hasText: 'Аватар обновлён.' })
    ).toBeVisible()
    await expect(account.avatarImage).toBeVisible()

    const src = await account.avatarImage.getAttribute('src')

    expect(src).toMatch(/^\/api\/media\/avatars\//)
    avatarSrc = src

    const publicProfile = new ProfilePage(page)

    await page.goto(`/barbers/${PROFILE_ID}`)
    await expect(
      page.getByRole('heading', { name: PROFILE_NAME, level: 1 })
    ).toBeVisible()

    await expect(publicProfile.heroAvatarImage).toHaveAttribute(
      'src',
      src ?? ''
    )
    await expect
      .poll(() =>
        publicProfile.heroAvatarImage.evaluate(
          (node) => (node as HTMLImageElement).naturalWidth
        )
      )
      .toBeGreaterThan(0)

    // Контракт публичной карточки отдаёт тот же относительный URL.
    const response = await page.request.get(`/api/barbers/${PROFILE_ID}`)

    expect(response.status(), 'GET /api/barbers/{id}').toBe(200)

    const body = (await response.json()) as { avatarUrl: string | null }

    expect(body.avatarUrl).toBe(src)

    // Поиск (#86): маркер изолирует карточку, в ней — тот же `src`, что в кабинете.
    const card = await searchMarkerCard(page)
    const cardImage = card.locator('img')

    await expect(cardImage).toHaveAttribute(
      'src',
      /^\/api\/media\/avatars\//
    )
    await expect(cardImage).toHaveAttribute('src', src ?? '')
    await expect(cardImage).toHaveAttribute('alt', '')
    await expect
      .poll(() =>
        cardImage.evaluate((node) => (node as HTMLImageElement).naturalWidth)
      )
      .toBeGreaterThan(0)

    expect(await searchMarkerAvatarUrl(page)).toBe(src)
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
