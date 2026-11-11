import { fileURLToPath } from 'node:url'

import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { deleteMyAvatar } from '../fixtures/api'
import { AccountProfilePage } from '../pages/account-profile-page'
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
// Кабинет считает инициалы по имени и фамилии аккаунта («Профиль Барберов»),
// а герой карточки — по displayName профиля («E2E Барбер Кабинета»): это разные
// источники, поэтому и константы разные.
const ACCOUNT_INITIALS = 'ПБ'
const HERO_INITIALS = 'EБ'

test.use({ storageState: storageStatePath('profileBarber') })

test.describe.configure({ mode: 'serial' })

test.describe('публичный аватар барбера: мутации', () => {
  let avatarSrc: string | null = null

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
  })
})
