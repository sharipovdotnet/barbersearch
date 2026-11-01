import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'
import { AdminReviewsPage } from '../pages/admin-reviews-page'
import { AdminUsersPage } from '../pages/admin-users-page'
import { ProfilePage } from '../pages/profile-page'

/**
 * axe на 320 px: узкий экран рендерит другую ветку разметки.
 *
 * На телефоне очередь и список пользователей живут без панели детали, поэтому
 * `aria-controls` проверяется именно здесь: ссылка не должна вести в пустоту.
 * Проект `narrow-320` (320×800) запускает этот файл вместе с `pager-320.spec.ts`.
 */
test.describe('axe: 320 px, публичный профиль', () => {
  test.use({ storageState: { cookies: [], origins: [] } })

  test('профиль барбера: пагинатор и иконочные кнопки', async ({ page }) => {
    const profile = new ProfilePage(page)

    await profile.goto()
    await profile.waitForFirstPage()
    await expect(profile.heading).toBeVisible()

    await expectNoBlockingViolations(
      page,
      `/barbers/${fixtures.barberProfileId} @320`,
      test.info()
    )
  })
})

test.describe('axe: 320 px, админка без выбранной строки', () => {
  test.use({ storageState: storageStatePath('admin') })

  test('очередь отзывов: без панели детали', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto()
    await expect(adminReviews.heading).toBeVisible()
    await expect(adminReviews.tab('На модерации')).toBeVisible()
    await expect(adminReviews.rows.first()).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/admin/reviews @320',
      test.info()
    )
  })

  test('пользователи: без панели детали', async ({ page }) => {
    const adminUsers = new AdminUsersPage(page)

    await adminUsers.goto()
    await adminUsers.waitForFirstPage()
    await expect(adminUsers.heading).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/users @320', test.info())
  })

  test('каталог: салоны карточками, без горизонтального скролла', async ({ page }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto('salons')
    await catalogue.waitForRows()
    await expect(catalogue.heading).toBeVisible()

    // На 320 таблица скрыта: карточки не должны расширять страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/admin/catalogue?tab=salons @320',
      test.info()
    )
  })
})
