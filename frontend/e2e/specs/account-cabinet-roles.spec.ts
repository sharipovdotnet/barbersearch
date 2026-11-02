import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AccountProfilePage } from '../pages/account-profile-page'

/**
 * Кабинет — для всех ролей: у клиента, барбера и администратора один каркас,
 * различаются только данные и ссылка на рабочую зону.
 */

test.describe('кабинет клиента', () => {
  test.use({ storageState: storageStatePath('customer') })

  test('открывает профиль и не показывает рабочую зону', async ({ page }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await expect(profile.identity).toContainText('Ева Клиентова')
    await expect(
      page.getByRole('navigation', { name: 'Разделы кабинета' })
    ).toContainText('Безопасность')
    await expect(
      page.getByRole('link', { name: 'Кабинет барбера' })
    ).toHaveCount(0)
    await expect(
      page.getByRole('link', { name: 'Администрирование' })
    ).toHaveCount(0)
  })
})

test.describe('кабинет барбера', () => {
  test.use({ storageState: storageStatePath('barber') })

  test('видит свой профиль и ссылку на кабинет барбера', async ({ page }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await expect(profile.identity).toContainText('Тимур Барберов')

    const workLink = profile.identity.getByRole('link', {
      name: 'Кабинет барбера',
    })
    await expect(workLink).toBeVisible()
    await expect(workLink).toHaveAttribute('href', '/barber')
  })
})

test.describe('кабинет администратора', () => {
  test.use({ storageState: storageStatePath('admin') })

  test('видит свой профиль и ссылку на администрирование', async ({ page }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await expect(profile.identity).toContainText('Арина Админова')

    const adminLink = profile.identity.getByRole('link', {
      name: 'Администрирование',
    })
    await expect(adminLink).toBeVisible()
    await expect(adminLink).toHaveAttribute('href', '/admin')
  })
})
