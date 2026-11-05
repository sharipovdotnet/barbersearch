import { expect, test } from '../fixtures/test'

import { baseURL, fixtures, storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { BecomeABarberPage } from '../pages/become-a-barber-page'

/**
 * Заявка «Стать барбером» (#46): клиент подаёт, админ отклоняет с причиной,
 * клиент читает причину.
 *
 * Проект `mutations-chromium` (суффикс `mutations.spec.ts`, идёт последним):
 * заявка остаётся в базе отклонённой, а сид удаляет заявки фикстур и возвращает
 * `e2e-applicant` роль `Customer`, поэтому повторный прогон снова начинает с
 * чистой формы, а не с `409 barberRequest.create.alreadyPending`.
 *
 * Одобрение здесь не проверяется: `ChangeRole` ротирует security stamp и сессия
 * заявителя умирает — этот путь покрыт unit/integration-тестами backend.
 * Админ работает в отдельном контексте: у заявителя своя сессия, и после решения
 * он остаётся клиентом.
 */
test.use({ storageState: storageStatePath('applicant') })

const REQUEST = fixtures.barberRequest

test.describe('заявка «Стать барбером»', () => {
  test('клиент подаёт заявку, админ отклоняет её с причиной, клиент видит причину', async ({
    page,
    browser,
  }, testInfo) => {
    const become = new BecomeABarberPage(page)

    await become.goto()
    await expect(become.heading).toBeVisible()
    await expect(become.formHeading).toBeVisible()

    await expectNoBlockingViolations(page, '/become-a-barber', testInfo)

    await become.submitRequest({ about: 'E2E: стригу с 2020 года' })

    // `201` вернул заявку, второй `GET /mine` для этого не нужен.
    await expect(become.status).toContainText('На рассмотрении')
    // Формы больше нет: одна активная заявка на пользователя.
    await expect(become.submit).toHaveCount(0)

    const adminContext = await browser.newContext({
      baseURL,
      storageState: storageStatePath('admin'),
    })
    const adminPage = await adminContext.newPage()

    try {
      await adminPage.goto('/admin/barber-requests')

      const row = adminPage.getByRole('link', {
        name: new RegExp(REQUEST.displayName),
      })

      await expect(row).toHaveCount(1)
      await row.click()

      const detail = adminPage.getByRole('region', {
        name: REQUEST.displayName,
      })

      await expect(detail).toBeVisible()
      await expect(detail).toContainText(REQUEST.city)

      await detail.getByRole('button', { name: 'Отклонить' }).click()

      const dialog = adminPage.getByRole('alertdialog')

      await expect(dialog).toBeVisible()
      await expect(dialog).toContainText('Отклонить заявку?')

      // Карточка и подтверждение — та же ветка разметки, что у очереди: сканируем
      // их здесь, пока заявка существует и диалог открыт.
      await expectNoBlockingViolations(
        adminPage,
        '/admin/barber-requests + диалог',
        testInfo
      )

      await dialog.getByLabel('Причина отказа').fill(REQUEST.rejectionReason)
      await dialog.getByRole('button', { name: 'Отклонить' }).click()

      await expect(adminPage.getByRole('status')).toContainText('отклонена')
      // Очередь перечитана: разобранная заявка в неё больше не входит.
      await expect(
        adminPage.getByText('В загруженной странице нет заявок.')
      ).toBeVisible()

      await expectNoBlockingViolations(
        adminPage,
        '/admin/barber-requests',
        testInfo
      )
    } finally {
      await adminContext.close()
    }

    // Заявитель видит решение и причину: статус читается с сервера.
    await become.goto()

    await expect(become.rejectedHeading).toBeVisible()
    await expect(become.status).toContainText('Отклонена')
    await expect(
      become.rejectionReason(REQUEST.rejectionReason)
    ).toBeVisible()
  })
})
