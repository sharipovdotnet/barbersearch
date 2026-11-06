import { expect, test } from '../fixtures/test'

import { expectNoBlockingViolations } from '../fixtures/axe'
import { CabinetPage } from '../pages/cabinet-page'
import { LoginDialog } from '../pages/login-dialog'

/**
 * Онбординг барбера (#45): роль `Barber` выдана, профиля ещё нет.
 *
 * Аккаунт `e2e-new-barber@…` приходит из сида без профиля, а сид каждый раз удаляет
 * профиль, созданный прошлым прогоном (§ `seed.sql`, 2.1). Сценарий входит через
 * диалог, а не через storageState роли: создание профиля продлевает сессию, и
 * refresh-токен должен быть выдан этим прогоном — переиспользованный storageState нёс
 * бы уже ротированный токен и получил бы терминальный отказ вместо кабинета.
 *
 * Ширины 1280 и 320 проверяются до отправки формы: после `201` экран показывает шаг
 * «Добавьте салон», а «Пропустить» ведёт в кабинет (#47).
 */
test.describe('онбординг барбера', () => {
  test('барбер без профиля создаёт его и попадает в кабинет', async ({
    page,
  }, testInfo) => {
    const login = new LoginDialog(page)
    const cabinet = new CabinetPage(page)

    // Тупик до среза: кабинет закрыт гейтом «обратитесь к администратору».
    await page.goto('/barber')
    await expect(
      page.getByText('Войдите, чтобы увидеть записи клиентов')
    ).toBeVisible()

    // Вход из гейта кабинета: после входа барбер остаётся в `/barber`.
    await page.getByRole('main').getByRole('button', { name: 'Войти' }).click()
    await expect(login.dialog).toBeVisible()
    await login.signInAs('newBarber')
    await login.expectClosed()

    const cta = page.getByRole('button', { name: 'Создать профиль барбера' })
    await expect(cta).toBeVisible()
    await cta.click()

    await expect(page).toHaveURL(/\/barber\/onboarding$/)
    await expect(
      page.getByRole('heading', { name: 'Профиль барбера', level: 1 })
    ).toBeVisible()

    await expectNoBlockingViolations(page, '/barber/onboarding', testInfo)

    await page.setViewportSize({ width: 320, height: 800 })

    // Длинные подписи и сетка чекбоксов переносятся, а не расширяют страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)
    await expectNoBlockingViolations(
      page,
      '/barber/onboarding @320',
      testInfo
    )

    await page.setViewportSize({ width: 1280, height: 900 })

    await page.getByLabel('Как вас представить').fill('E2E Новый Барбер')
    await page.getByLabel('Опыт, лет').fill('6')
    await page.getByLabel('Грейд').selectOption('MiddleBarber')
    await page.getByRole('checkbox', { name: 'Русский' }).click()
    await page.getByRole('checkbox', { name: 'Карта' }).click()

    await page.getByRole('button', { name: 'Создать профиль' }).click()

    // `201` → refresh (новый claim `barber_profile_id`) → шаг салона; он не блокирует
    // кабинет, поэтому «Пропустить» сразу ведёт на `/barber`.
    await expect(
      page.getByText('Добавьте салон', { exact: true })
    ).toBeVisible()
    await page.getByRole('button', { name: 'Пропустить' }).click()

    await expect(page).toHaveURL(/\/barber$/)
    await expect(cabinet.heading).toBeVisible()
    // Профиль создан, салонов и услуг ещё нет: записи придут позже.
    // Текст есть и в тихом `aria-live`-регионе, поэтому берём видимую ветку панели.
    await expect(
      page
        .getByRole('tabpanel', { name: 'Ближайшие' })
        .getByText('Ближайших записей нет.')
    ).toBeVisible()
  })
})
