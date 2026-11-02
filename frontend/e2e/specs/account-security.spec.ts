import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AccountSecurityPage } from '../pages/account-security-page'

/** `/account/security` — пароль и выход; мутаций нет, пароль не меняем. */
test.use({ storageState: storageStatePath('customer') })

test.describe('кабинет: безопасность', () => {
  test('показывает пароль, выход и подтверждение email', async ({ page }) => {
    const security = new AccountSecurityPage(page)

    await security.goto()
    await security.waitForReady()

    await expect(security.emailCard).toContainText(
      'e2e-customer@barbersearch.test'
    )
    await expect(security.emailCard).toContainText('Адрес подтверждён')
  })

  test('пустая форма смены пароля показывает ошибки и закрывается по Escape', async ({
    page,
  }) => {
    const security = new AccountSecurityPage(page)

    await security.goto()
    await security.waitForReady()
    await security.changePassword.click()

    const dialog = security.passwordDialog
    await expect(dialog).toBeVisible()
    await expect(
      dialog.getByRole('heading', { name: 'Смена пароля' })
    ).toBeVisible()

    await dialog.getByRole('button', { name: 'Сменить пароль' }).click()
    await expect(dialog.getByText('Введите текущий пароль.')).toBeVisible()
    await expect(dialog.getByText('Введите новый пароль.')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(dialog).toBeHidden()
  })
})
