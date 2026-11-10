import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { BarberProfilePage } from '../pages/barber-profile-page'

/**
 * `/barber/profile` — чтение (§5.9).
 *
 * Отдельный барбер `profileBarber`: его профиль подтверждён, но прайс и
 * специализации пусты. Витринного барбера здесь не трогаем — на нём держатся
 * брони, отзывы и счётчики других спек.
 */
test.use({ storageState: storageStatePath('profileBarber') })

test.describe('профиль барбера', () => {
  test('форма заполнена из API, прайс и специализации пусты', async ({ page }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await expect(
      page.getByRole('heading', { name: 'Профиль', level: 1 })
    ).toBeVisible()
    await expect(
      page.getByText(
        'Карточка, которую видят клиенты: услуги, цены и специализации.'
      )
    ).toBeVisible()

    // Поля приходят из `GET /api/barber-profiles/{id}`, а не из кэша публичной
    // карточки: она отдаёт и скрытые услуги, и неподтверждённый профиль.
    await expect(profile.displayName).toHaveValue('E2E Барбер Кабинета')
    await expect(profile.experience).toHaveValue('7')
    await expect(profile.bio).toHaveValue(
      'E2E-профиль: карточка, услуги и специализации.'
    )
    await expect(profile.latePolicy).toHaveValue('10 minutes')

    // Форма dirty-only: без правок сохранять нечего.
    await expect(profile.saveProfile).toBeDisabled()

    await expect(profile.emptyServices).toBeVisible()
    await expect(profile.servicesList).toHaveCount(0)
    await expect(profile.emptySpecializations).toBeVisible()

    // Подтверждённый и активный профиль: клиентская ссылка ведёт на публичную карточку.
    await expect(profile.publicLink).toHaveAttribute(
      'href',
      `/barbers/${fixtures.profileBarber.profileId}`
    )

    // Четыре раздела кабинета: записи, расписание, салоны и профиль.
    await expect(profile.nav.getByRole('link')).toHaveCount(4)
    await expect(profile.navLink('Профиль')).toHaveAttribute(
      'aria-current',
      'page'
    )
  })

  test('четвёртый пункт навигации ведёт на /barber/profile', async ({ page }) => {
    // Начинаем с соседнего раздела: ссылка должна быть настоящим переходом,
    // а не подменой заголовка на текущей странице.
    await page.goto('/barber/schedule')
    await expect(
      page.getByRole('heading', { name: 'Расписание', level: 1 })
    ).toBeVisible()

    const profile = new BarberProfilePage(page)

    await profile.navLink('Профиль').click()

    await expect(page).toHaveURL(/\/barber\/profile$/)
    await profile.waitForReady()
    await expect(profile.navLink('Профиль')).toHaveAttribute(
      'aria-current',
      'page'
    )
    await expect(profile.navLink('Расписание')).not.toHaveAttribute(
      'aria-current',
      'page'
    )
  })
})
