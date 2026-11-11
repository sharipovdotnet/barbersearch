import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { AdminAuditPage } from '../pages/admin-audit-page'
import { AdminBookingsPage } from '../pages/admin-bookings-page'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'
import { AdminReviewsPage } from '../pages/admin-reviews-page'
import { AdminStatisticsPage } from '../pages/admin-statistics-page'
import { AdminSummaryPage } from '../pages/admin-summary-page'
import { AdminUsersPage } from '../pages/admin-users-page'
import { AccountProfilePage } from '../pages/account-profile-page'
import { AccountSecurityPage } from '../pages/account-security-page'
import { BarberNotificationsPage } from '../pages/barber-notifications-page'
import { BarberProfilePage } from '../pages/barber-profile-page'
import { CabinetPage } from '../pages/cabinet-page'
import { ForgotPasswordPage } from '../pages/forgot-password-page'
import { LandingPage } from '../pages/landing-page'
import { MyReviewsPage } from '../pages/my-reviews-page'
import { ProfilePage } from '../pages/profile-page'
import { ResetPasswordPage } from '../pages/reset-password-page'
import { VerifyEmailPage } from '../pages/verify-email-page'

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

  test('подтверждение email /verify-email: недействительная ссылка', async ({
    page,
  }) => {
    const verify = new VerifyEmailPage(page)

    // Анонимный экран: токен битый, поэтому страница сразу в состоянии `invalid`,
    // без валидного токена и без создания аккаунта. Успех сканируется в
    // `email-verification.spec.ts`, 320 px — в `a11y-narrow.spec.ts`.
    await verify.goto('broken-token-from-axe')
    await verify.waitForInvalid()

    await expectNoBlockingViolations(page, '/verify-email', test.info())
  })

  test('восстановление пароля /forgot-password: форма', async ({ page }) => {
    const forgot = new ForgotPasswordPage(page)

    // Только форма: любой submit — auth-запрос, а лимит (10/мин) общий на IP и
    // делится с функциональным спеком сброса (§6). Состояние «sent» сканируется
    // там же, где проверяется сценарий, а не в axe-тесте.
    await forgot.goto()
    await forgot.waitForReady()

    await expectNoBlockingViolations(page, '/forgot-password', test.info())
  })

  test('новый пароль /reset-password: форма с фейковым токеном', async ({
    page,
  }) => {
    const reset = new ResetPasswordPage(page)

    // Фейковый токен: страница не делает preflight и не ходит в API до submit,
    // поэтому форма рендерится без auth-запроса. Токен сразу исчезает из адреса.
    await reset.goto('broken-token-from-axe')
    await reset.waitForForm()

    await expectNoBlockingViolations(page, '/reset-password', test.info())
  })

  test('«Стать барбером» /become-a-barber: анонимный экран', async ({ page }) => {
    await page.goto('/become-a-barber')
    await expect(
      page.getByRole('heading', { name: 'Стать барбером', level: 1 })
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { name: 'Что даёт статус барбера' })
    ).toBeVisible()

    await expectNoBlockingViolations(page, '/become-a-barber', test.info())
  })

  test('политика обработки данных /privacy и уведомление о cookie', async ({
    page,
  }) => {
    await page.goto('/privacy')
    await expect(
      page.getByRole('heading', {
        name: 'Политика в отношении обработки персональных данных',
        level: 1,
      })
    ).toBeVisible()

    const notice = page.getByRole('region', {
      name: 'Уведомление об использовании cookie',
    })
    await expect(notice).toBeVisible()

    // Уведомление информационное: одна кнопка, без стены согласия (раздел 6 Политики).
    await page.getByRole('button', { name: 'Понятно' }).click()
    await expect(notice).toBeHidden()

    await expectNoBlockingViolations(page, '/privacy', test.info())
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
    // Карточка заявки читает `/barber-requests/mine` отдельно: сканируем уже
    // загруженное состояние, а не «Проверяем заявку…».
    await expect(
      page.getByRole('button', { name: 'Стать барбером' })
    ).toBeVisible()

    await expectNoBlockingViolations(page, '/account', test.info())
  })

  test('/account/reviews: авторские отзывы и пагинатор', async ({ page }) => {
    const myReviews = new MyReviewsPage(page)

    await myReviews.goto()
    await myReviews.waitForFirstPage()
    await expect(myReviews.heading).toBeVisible()

    await expectNoBlockingViolations(page, '/account/reviews', test.info())
  })

  test('/account/profile: форма и данные аккаунта', async ({ page }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    await expectNoBlockingViolations(page, '/account/profile', test.info())
  })

  test('/account/security: пароль, выход и email', async ({ page }) => {
    const security = new AccountSecurityPage(page)

    await security.goto()
    await security.waitForReady()

    await expectNoBlockingViolations(page, '/account/security', test.info())
  })
})

test.describe('axe: кабинет барбера', () => {
  test.use({ storageState: storageStatePath('barber') })

  test('/barber: лента дня, вкладки и строки', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await expect(cabinet.heading).toBeVisible()
    await expect(cabinet.tablist).toBeVisible()
    await expect(cabinet.dayHeading).toBeVisible()

    await expectNoBlockingViolations(page, '/barber (сегодня)', test.info())

    await cabinet.waitForUpcoming()
    await expectNoBlockingViolations(
      page,
      '/barber (ближайшие)',
      test.info()
    )
  })

  test('/barber: диалог записи клиента', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await cabinet.openBookingDialog()
    await expect(cabinet.bookingDialog).toBeVisible()

    await expectNoBlockingViolations(page, '/barber (запись клиента)', test.info())
  })

  test('/barber/notifications: настройки и строки инбокса', async ({ page }) => {
    const notifications = new BarberNotificationsPage(page)

    await notifications.goto()
    // Настройки и обе строки уже загружены: скан не должен попасть на скелетон.
    await notifications.waitForReady()

    await expectNoBlockingViolations(
      page,
      '/barber/notifications',
      test.info()
    )
  })

  test('/barber/salons: карточки и открытый диалог каталога', async ({ page }) => {
    await page.goto('/barber/salons')
    await expect(
      page.getByRole('heading', { name: 'Мои салоны', level: 1 })
    ).toBeVisible()

    await page.getByRole('button', { name: 'Добавить салон' }).click()

    const dialog = page.getByRole('dialog')

    await expect(dialog).toBeVisible()
    await dialog.getByLabel('Поиск').fill('E2E Studio')
    await expect(dialog.getByRole('radio').first()).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/barber/salons + диалог',
      test.info()
    )
  })
})

test.describe('axe: профиль барбера', () => {
  // Отдельный изолированный профиль: у него пустой прайс, поэтому скан
  // проверяет и разделы с пустыми состояниями, а не только форму.
  test.use({ storageState: storageStatePath('profileBarber') })

  test('/barber/profile: карточка, пустой прайс и специализации', async ({
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    // Ждём загруженную форму, а не скелетон: скелетон тоже `role="status"`.
    await profile.waitForReady()
    await expect(profile.emptyServices).toBeVisible()
    await expect(profile.emptySpecializations).toBeVisible()

    await expectNoBlockingViolations(page, '/barber/profile', test.info())
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

    // По умолчанию открыта очередь «На проверке», а в фикстурах подтверждены
    // все профили, кроме одного (B14, секция 4.1 сида): список берём фильтром «Все».
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

  test('/admin/users/{id}: карточка без фото, блок аватара', async ({ page }) => {
    // Прямой адрес карточки: split-view на 1280 тоже открывает деталь, но гейт
    // должен покрывать маршрут с id отдельно — бейдж и диалог аватара живут там.
    await page.goto(`/admin/users/${fixtures.mutationsUserId}`)

    await expect(page.locator('#user-detail')).toBeVisible()
    await expect(page.locator('#user-detail-title')).toHaveText('Марк Правкин')
    // Сид сбрасывает `avatar_url`, поэтому блок модерации показывает подсказку
    // «скрывать нечего», а не кнопку с диалогом.
    await expect(
      page.getByText('У пользователя нет фото — скрывать нечего.')
    ).toBeVisible()

    await expectNoBlockingViolations(
      page,
      `/admin/users/${fixtures.mutationsUserId}`,
      test.info()
    )
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

  test('/admin/bookings: поиск, список и карточка брони', async ({ page }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    // Поиск по клиенту фикстур: строки не зависят от чужих броней базы.
    await adminBookings.searchFor(fixtures.adminBookings.email)
    await adminBookings.waitForRows(fixtures.adminBookings.pageSize)
    // NN 24 — самая поздняя бронь: выбранная карточка входит в тот же скан.
    await adminBookings.openBooking(fixtures.adminBookings.latestId)

    await expect(adminBookings.heading).toBeVisible()
    await expect(adminBookings.detailHeading).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/admin/bookings + карточка',
      test.info()
    )
  })

  test('/admin/summary: три блока со счётчиками и ссылками', async ({ page }) => {
    const summary = new AdminSummaryPage(page)

    await summary.goto()
    await expect(summary.heading).toBeVisible()
    await expect(summary.reviewsBlock).toBeVisible()
    await expect(summary.barbersBlock).toBeVisible()
    await expect(summary.salonsBlock).toBeVisible()

    await expectNoBlockingViolations(page, '/admin/summary', test.info())
  })

  test('/admin/statistics: плитки, график и таблица', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()
    await expect(statistics.heading).toBeVisible()
    await expect(statistics.chart).toHaveCount(1)

    // Раскрытая таблица-дубль проверяется отдельным сканом: это тот же экран,
    // но с ещё одним интерактивным слоем внутри `<details>`.
    await statistics.tableToggle.click()
    await expect(statistics.table).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/admin/statistics + таблица',
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

  // Пустая очередь — состояние, в котором раздел открывается после reseed:
  // заявку создаёт только мутирующий спек, а он идёт последним.
  test('/admin/barber-requests: пустая очередь', async ({ page }) => {
    await page.goto('/admin/barber-requests')
    await expect(
      page.getByRole('heading', { name: 'Заявки', level: 1 })
    ).toBeVisible()
    await expect(page.getByText('Заявок нет')).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/admin/barber-requests',
      test.info()
    )
  })
})

test.describe('axe: заявка на роль барбера', () => {
  // e2e-applicant после сида без заявки: это ровно форма, а не статус.
  test.use({ storageState: storageStatePath('applicant') })

  test('форма клиента без заявки', async ({ page }) => {
    await page.goto('/become-a-barber')
    await expect(
      page.getByRole('heading', { name: 'Заявка на роль барбера' })
    ).toBeVisible()
    await expect(page.getByLabel('Как вас представить')).toBeVisible()

    await expectNoBlockingViolations(
      page,
      '/become-a-barber (форма)',
      test.info()
    )
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
