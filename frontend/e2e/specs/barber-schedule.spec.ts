import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'

/**
 * `/barber/schedule` — редактирование расписания (§4.4).
 *
 * Читающий спек: сид задаёт витринному барберу неделю 09:00–18:00, и мутировать
 * её здесь нельзя — от неё зависят слоты и брони других сценариев. Проверяем,
 * что экран открылся, неделя пришла из API и вкладки ведут в оба раздела кабинета.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('расписание барбера', () => {
  test('неделя, исключения и отпуска, axe', async ({ page }) => {
    await page.goto('/barber/schedule')

    await expect(
      page.getByRole('heading', { name: 'Расписание', level: 1 })
    ).toBeVisible()
    await expect(
      page.getByText('Недельное расписание', { exact: true })
    ).toBeVisible()
    await expect(
      page.getByText('Исключения на даты', { exact: true })
    ).toBeVisible()
    await expect(
      page.getByText('Отпуска и нерабочие периоды', { exact: true })
    ).toBeVisible()

    await expect(
      page.getByLabel('Начало работы, Понедельник')
    ).toHaveValue('09:00')
    await expect(
      page.getByLabel('Окончание работы, Понедельник')
    ).toHaveValue('18:00')

    await expect(page.getByRole('link', { name: 'Записи' })).toHaveAttribute(
      'href',
      '/barber'
    )
    await expect(
      page.getByRole('link', { name: 'Расписание' })
    ).toHaveAttribute('href', '/barber/schedule')

    await expectNoBlockingViolations(
      page,
      '/barber/schedule',
      test.info()
    )
  })
})
