import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AdminAuditPage } from '../pages/admin-audit-page'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'
import { AdminReviewsPage } from '../pages/admin-reviews-page'
import { AdminUsersPage } from '../pages/admin-users-page'
import { CabinetPage } from '../pages/cabinet-page'
import { LandingPage } from '../pages/landing-page'
import { MyReviewsPage } from '../pages/my-reviews-page'
import { ProfilePage } from '../pages/profile-page'

/**
 * axe на 1280: матрица экранов и ролей (§6).
 *
 * Блокируют только `critical`/`serious` (`fixtures/axe.ts`). Сканируем после
 * того, как экран устоялся: заголовок, первая страница списка и карточка детали
 * уже в DOM, а не скелетон. Один `analyze()` на экран: больше в одном тесте
 * не нужно и удваивает прогон.
 *
 * На широком экране админские split-view показывают список и карточку детали
 * одновременно, поэтому один скан доказывает обе ветки матрицы: очередь/список
 * и карточку с id в адресе.
 */
test.describe('axe: публичные экраны', () => {
  test.use({ storageState: { cookies: [], origins: [] } })

  test('витрина /', async ({ page }) => {
    const landing = new LandingPage(page)

    await landing.goto()
    await expect(landing.heading).toBeVisible()

    await expectNoBlockingViolations(page, '/', test.info())
  })

  test('профиль барбера с отзывами и пагинатором', async ({ page }) => {
    const profile = new ProfilePage(page)

    await profile.goto()
    await profile.waitForFirstPage()
    await expect(profile.heading).toBeVisible()

    await expectNoBlockingViolations(
      page,
      `/barbers/${fixtures.barberProfileId}`,
      test.info()
    )
  })

  test('регистрация /register', async ({ page }) => {
    await page.goto('/register')
    await expect(
      page.getByRole('heading', { name: 'Регистрация', level: 1 })
    ).toBeVisible()

    await expectNoBlockingViolations(page, '/register', test.info())
  })

  test('открытый диалог входа', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('button', { name: 'Войти' }).first().click()

    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()

    await expectNoBlockingViolations(page, 'LoginDialog', test.info())
  })
})

test.describe('axe: кабинет клиента', () => {
  test.use({ storageState: storageStatePath('customer') })

  test('/account: записи и вкладки', async ({ page }) => {
    await page.goto('/account')
    await expect(
      page.getByRole('heading', { name: 'Мои записи', level: 1 })
    ).toBeVisible()
    await expect(page.locator('#bookings-panel')).toBeVisible()

    await expectNoBlockingViolations(page, '/account', test.info())
  })

  test('/account/reviews: авторские отзывы и пагинатор', async ({ page }) => {
    const myReviews = new MyReviewsPage(page)

    await myReviews.goto()
    await myReviews.waitForFirstPage()
    await expect(myReviews.heading).toBeVisible()

    await expectNoBlockingViolations(page, '/account/reviews', test.info())
  })
})

test.describe('axe: кабинет барбера', () => {
  test.use({ storageState: storageStatePath('barber') })

  test('/barber: вкладки, строки и пагинатор', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await expect(cabinet.heading).toBeVisible()
    await expect(cabinet.tablist).toBeVisible()

    await expectNoBlockingViolations(page, '/barber', test.info())
  })
})

test.describe('axe: админка', () => {
  test.use({ storageState: storageStatePath('admin') })

  test('/admin/reviews: очередь и карточка отзыва', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto()
    await expect(adminReviews.heading).toBeVisible()
    await expect(adminReviews.tab('На модерации')).toBeVisible()
    await expect(adminReviews.rows.first()).toBeVisible()
    await expect(page.locator('#review-detail')).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/reviews', test.info())
  })

  test('/admin/reviews?tab=published: опубликованные', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto('published')
    await adminReviews.waitForFirstPage()
    await expect(adminReviews.tab('Опубликованные')).toHaveAttribute(
      'aria-selected',
      'true'
    )
    await expect(page.locator('#review-detail')).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/admin/reviews?tab=published',
      test.info()
    )
  })

  test('/admin/barbers: список и карточка барбера', async ({ page }) => {
    await page.goto('/admin/barbers')
    await expect(
      page.getByRole('heading', { name: 'Барберы', level: 1 })
    ).toBeVisible()

    // По умолчанию открыта очередь «На проверке», а в фикстурах все профили
    // подтверждены: список берём фильтром «Все».
    await page
      .getByRole('group', { name: 'Каких барберов показать' })
      .getByRole('button', { name: 'Все', exact: true })
      .click()

    const rows = page.locator('section[aria-label="Барберы"] tbody tr')
    await expect(rows.first()).toBeVisible()
    await expect(page.locator('#barber-detail')).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/barbers', test.info())
  })

  test('/admin/users: список и карточка пользователя', async ({ page }) => {
    const adminUsers = new AdminUsersPage(page)

    await adminUsers.goto()
    await adminUsers.waitForFirstPage()
    await expect(adminUsers.heading).toBeVisible()
    await expect(page.locator('#user-detail')).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/users', test.info())
  })

  test('/admin/catalogue: вкладка «Услуги»', async ({ page }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto()
    await catalogue.waitForRows()
    await expect(catalogue.heading).toBeVisible()
    await expect(catalogue.tabs).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/catalogue', test.info())
  })

  test('/admin/catalogue?tab=salons: широкая таблица и форма правки', async ({
    page,
  }) => {
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto('salons')
    await catalogue.waitForRows()
    await catalogue
      .editButton(fixtures.catalogueLabels.hiddenSalon)
      .click()
    await expect(catalogue.dialog).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/admin/catalogue?tab=salons + форма',
      test.info()
    )
  })

  test('/admin/audit: журнал с раскрытым diff', async ({ page }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    // Фильтр по фиксированной дате: строки фикстур не зависят от мутирующих спек.
    await audit.filterByDate(fixtures.journal.day)
    await audit.waitForFirstPage()
    await expect(audit.heading).toBeVisible()

    await audit.expandButton(fixtures.journal.roleChangeRowId).click()
    await expect(audit.diff(fixtures.journal.roleChangeRowId)).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/audit + diff', test.info())
  })
})

test.describe('клавиатура', () => {
  test.use({ storageState: { cookies: [], origins: [] } })

  test('Tab до пагинатора, Enter и Пробел листают, фокус-кольцо видно', async ({
    page,
  }) => {
    const profile = new ProfilePage(page)

    await profile.goto()
    await profile.waitForFirstPage()

    const next = profile.pager.next

    // Обход по Tab, а не программный focus(): проверяем реальный порядок табуляции.
    let reached = false

    for (let i = 0; i < 250; i += 1) {
      await page.keyboard.press('Tab')

      if (await next.evaluate((element) => element === document.activeElement)) {
        reached = true
        break
      }
    }

    expect(reached, 'кнопка «Дальше» достижима с клавиатуры').toBe(true)
    await expect(next).toBeFocused()

    // Кольцо фокуса видно: у кнопки сохранён :focus-visible (§11).
    const focusVisible = await next.evaluate((element) =>
      element.matches(':focus-visible')
    )

    expect(focusVisible, 'фокус-кольцо видно на кнопке пагинатора').toBe(true)

    await page.keyboard.press('Enter')
    await profile.pager.expectRange('11–12 из 12')

    // После смены страницы фокус уходит на список; Tab возвращает к пагинатору.
    await expect(profile.reviewsList).toBeFocused()

    const previous = profile.pager.previous
    let backToPager = false

    for (let i = 0; i < 10; i += 1) {
      await page.keyboard.press('Tab')

      if (
        await previous.evaluate((element) => element === document.activeElement)
      ) {
        backToPager = true
        break
      }
    }

    expect(backToPager, 'Tab от списка возвращает к пагинатору').toBe(true)

    await page.keyboard.press('Space')
    await profile.pager.expectRange('1–10 из 12')
  })

  test('диалог входа закрывается Escape и возвращает фокус на триггер', async ({
    page,
  }) => {
    await page.goto('/')

    const trigger = page.getByRole('button', { name: 'Войти' }).first()
    await trigger.click()

    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()

    await page.keyboard.press('Escape')

    await expect(dialog).toBeHidden()
    await expect(trigger).toBeFocused()
  })
})
