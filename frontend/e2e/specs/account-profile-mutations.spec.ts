import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AccountProfilePage } from '../pages/account-profile-page'

/**
 * Профиль кабинета: правка имени сохраняется на сервере и сразу видна в каркасе.
 *
 * Мутирующая сцена, поэтому отдельный клиент `mutations`: имя фикстуры не должно
 * влиять на читающие спеки кабинета. Следующий `npm run test:e2e:seed` вернёт
 * исходное значение.
 */
test.use({ storageState: storageStatePath('mutations') })

test.describe('кабинет: профиль', () => {
  test('сохранение имени обновляет рейл и переживает reload', async ({ page }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    const name = `Марк-${Date.now()}`

    await profile.firstName.fill(name)
    await expect(profile.save).toBeEnabled()
    await profile.save.click()

    await expect(profile.status).toContainText('Изменения сохранены.')
    await expect(profile.identity).toContainText(name)

    await page.reload()
    await profile.waitForReady()
    await expect(profile.firstName).toHaveValue(name)
  })

  test('пустое имя не отправляется', async ({ page }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await profile.firstName.fill('')
    await profile.save.click()

    await expect(page.getByText('Введите имя.')).toBeVisible()
  })
})
