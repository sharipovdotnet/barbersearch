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
import { BarberNotificationsPage } from '../pages/barber-notifications-page'
import { BarberProfilePage } from '../pages/barber-profile-page'
import { CabinetPage } from '../pages/cabinet-page'
import { ForgotPasswordPage } from '../pages/forgot-password-page'
import { ProfilePage } from '../pages/profile-page'
import { ResetPasswordPage } from '../pages/reset-password-page'
import { VerifyEmailPage } from '../pages/verify-email-page'

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

  test('политика обработки данных: перечень cookie на 320', async ({ page }) => {
    await page.goto('/privacy')
    await expect(
      page.getByRole('heading', {
        name: 'Политика в отношении обработки персональных данных',
        level: 1,
      })
    ).toBeVisible()

    // Длинные имена cookie и реквизиты Оператора переносятся, а не расширяют страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/privacy @320', test.info())
  })

  test('«Стать барбером»: анонимный экран на 320', async ({ page }) => {
    await page.goto('/become-a-barber')
    await expect(
      page.getByRole('heading', { name: 'Стать барбером', level: 1 })
    ).toBeVisible()
    await expect(
      page.getByRole('heading', { name: 'Что даёт статус барбера' })
    ).toBeVisible()

    // Две кнопки столбцом и длинный текст не должны расширять страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/become-a-barber @320',
      test.info()
    )
  })

  test('подтверждение email: карточка ошибки на 320', async ({ page }) => {
    const verify = new VerifyEmailPage(page)

    await verify.goto('broken-token-from-axe')
    await verify.waitForInvalid()

    // Длинный текст ошибки и две кнопки действий переносятся, а не дают scroll.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/verify-email (недействительная ссылка) @320',
      test.info()
    )
  })

  test('восстановление пароля: форма на 320', async ({ page }) => {
    const forgot = new ForgotPasswordPage(page)

    // Форма без submit: каждый submit — auth-запрос, лимит общий на IP (§6).
    await forgot.goto()
    await forgot.waitForReady()

    // Поле, подсказка и кнопка переносятся, а не дают горизонтальный скролл.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/forgot-password @320', test.info())
  })

  test('новый пароль: форма на 320', async ({ page }) => {
    const reset = new ResetPasswordPage(page)

    // Фейковый токен: до submit страница не обращается к API, поэтому скан
    // не тратит auth-лимит. Токен сразу исчезает из адреса.
    await reset.goto('broken-token-from-axe')
    await reset.waitForForm()

    // Подсказка «Минимум 12 символов…» и два поля не расширяют страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/reset-password @320', test.info())
  })
})

test.describe('axe: 320 px, заявка на роль барбера', () => {
  test.use({ storageState: storageStatePath('applicant') })

  test('форма клиента без заявки на 320', async ({ page }) => {
    await page.goto('/become-a-barber')
    await expect(
      page.getByRole('heading', { name: 'Заявка на роль барбера' })
    ).toBeVisible()
    await expect(page.getByLabel('Как вас представить')).toBeVisible()

    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/become-a-barber (форма) @320',
      test.info()
    )
  })
})

test.describe('axe: 320 px, расписание барбера', () => {
  test.use({ storageState: storageStatePath('barber') })

  test('/barber: лента дня не расширяет страницу', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await expect(cabinet.dayHeading).toBeVisible()
    await expect(cabinet.tablist).toBeVisible()

    // Три вкладки в одну строку: уменьшается отступ, а не появляется scroll.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/barber @320', test.info())
  })

  test('неделя, исключения и отпуска не расширяют страницу', async ({ page }) => {
    await page.goto('/barber/schedule')
    await expect(
      page.getByRole('heading', { name: 'Расписание', level: 1 })
    ).toBeVisible()
    await expect(
      page.getByText('Недельное расписание', { exact: true })
    ).toBeVisible()

    // Два времени в строке дня и две даты отпуска переносятся, а не дают scroll.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/barber/schedule @320',
      test.info()
    )
  })

  test('«Мои салоны» и диалог каталога на 320', async ({ page }) => {
    await page.goto('/barber/salons')
    await expect(
      page.getByRole('heading', { name: 'Мои салоны', level: 1 })
    ).toBeVisible()
    await expect(
      page.getByRole('button', { name: 'Добавить салон' }).first()
    ).toBeVisible()

    await page.getByRole('button', { name: 'Добавить салон' }).first().click()

    const dialog = page.getByRole('dialog')

    await expect(dialog).toBeVisible()
    await dialog.getByLabel('Поиск').fill('E2E Studio')
    await expect(dialog.getByRole('radio').first()).toBeVisible()

    // Длинные название, адрес и часовой пояс не должны расширять страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/barber/salons + диалог @320',
      test.info()
    )
  })
})

test.describe('axe: 320 px, профиль барбера', () => {
  test.use({ storageState: storageStatePath('profileBarber') })

  test('карточка и пустой прайс: без горизонтального скролла, действия 44 px', async ({
    page,
  }) => {
    const profile = new BarberProfilePage(page)

    await profile.goto()
    await profile.waitForReady()
    await expect(profile.emptyServices).toBeVisible()

    // Длинные подписи и сетка 2×2 навигации не должны расширять страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    // Действия на узком экране — 44 px: навигация, сохранение карточки,
    // добавление услуги и клиентская ссылка. Диалоги здесь не открываем:
    // пустой прайс и axe-скан не должны тянуть ленивый справочник.
    const actions = [
      profile.nav.getByRole('link', { name: 'Записи', exact: true }),
      profile.nav.getByRole('link', { name: 'Расписание', exact: true }),
      profile.nav.getByRole('link', { name: 'Салоны', exact: true }),
      profile.nav.getByRole('link', { name: 'Профиль', exact: true }),
      profile.saveProfile,
      profile.addServiceButton,
      profile.publicLink,
    ]

    for (const action of actions) {
      const box = await action.boundingBox()

      expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)
    }

    await expectNoBlockingViolations(
      page,
      '/barber/profile @320',
      test.info()
    )
  })
})

test.describe('axe: 320 px, уведомления барбера', () => {
  test.use({ storageState: storageStatePath('barber') })

  test('инбокс не расширяет страницу, колокольчик не сжимается', async ({
    page,
  }) => {
    const notifications = new BarberNotificationsPage(page)

    await notifications.goto()
    await notifications.waitForReady()

    // Настройки, две строки и длинные «стрижка · «Барбершоп на Ленина»» переносятся,
    // а не дают горизонтальный скролл.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/barber/notifications @320',
      test.info()
    )

    // Вход в инбокс — колокольчик 44×44 в заголовке кабинета (§8.1): на 320 px он
    // не сжимается, переносится заголовок рядом.
    const cabinet = new CabinetPage(page)

    await cabinet.goto()

    const bell = cabinet.notificationsBell

    await expect(bell).toBeVisible()
    // Счётчик здесь не проверяется: проект narrow-320 не зависит от mutations и может
    // пересечься с «Прочитать все». Важно, что ссылка и её имя на месте.
    await expect(bell).toHaveAttribute('aria-label', /^Уведомления/)

    const box = await bell.boundingBox()

    expect(box?.width ?? 0).toBeGreaterThanOrEqual(44)
    expect(box?.height ?? 0).toBeGreaterThanOrEqual(44)

    const cabinetOverflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(cabinetOverflow).toBeLessThanOrEqual(0)
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

    // Аватар 24 px и бейдж «Фото скрыто» не должны выдавливать имя из строки:
    // карточный список не расширяет страницу (§15).
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/admin/users @320', test.info())
  })

  test('барберы: карточки, без горизонтального скролла', async ({ page }) => {
    await page.goto('/admin/barbers')
    await expect(
      page.getByRole('heading', { name: 'Барберы', level: 1 })
    ).toBeVisible()

    // На 320 таблица скрыта, строки — карточки; по умолчанию открыта очередь
    // «На проверке», поэтому переключаемся на «Все», как в широкой axe-матрице.
    await page
      .getByRole('group', { name: 'Каких барберов показать' })
      .getByRole('button', { name: 'Все', exact: true })
      .click()

    const rows = page.locator('section[aria-label="Барберы"] ul > li')

    await expect(rows.first()).toBeVisible()

    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/admin/barbers @320', test.info())
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

  test('брони: поиск по клиенту, карточки без горизонтального скролла', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    // На 320 таблица заменена карточками, а строки появляются поиском по клиенту
    // фикстур: без него список состоит из чужих броней базы.
    await adminBookings.searchFor(fixtures.adminBookings.email)
    await adminBookings.waitForRows(fixtures.adminBookings.pageSize)
    await expect(adminBookings.heading).toBeVisible()
    await expect(adminBookings.rows.first()).toBeVisible()

    // Длинный email и id брони переносятся, а не расширяют страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/admin/bookings @320', test.info())
  })

  test('сводка: три блока одним столбцом, без горизонтального скролла', async ({
    page,
  }) => {
    const summary = new AdminSummaryPage(page)

    await summary.goto()
    await expect(summary.heading).toBeVisible()
    await expect(summary.reviewsBlock).toBeVisible()

    // Длинные имена клиента и барбера переносятся, а не расширяют страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/admin/summary @320', test.info())
  })

  test('статистика: плитки стопкой, без горизонтального скролла', async ({
    page,
  }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()
    await expect(statistics.heading).toBeVisible()
    await expect(statistics.chart).toHaveCount(1)

    // Плитки на 320 — стопка, таблица лидеров скроллится внутри контейнера,
    // а страница не должна получить горизонтальный скролл.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/admin/statistics @320', test.info())
  })

  test('журнал: строки с фильтром по дате, без горизонтального скролла', async ({
    page,
  }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    // Без фильтра журнал может быть пуст до мутирующих спек — фильтруем фикстуры.
    await audit.filterByDate(fixtures.journal.day)
    await audit.waitForFirstPage()
    await expect(audit.heading).toBeVisible()
    await expect(audit.rows.first()).toBeVisible()

    // Длинный email автора и diff «from → to» не должны расширять страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/admin/audit @320', test.info())
  })

  test('заявки: пустая очередь на 320', async ({ page }) => {
    await page.goto('/admin/barber-requests')
    await expect(
      page.getByRole('heading', { name: 'Заявки', level: 1 })
    ).toBeVisible()
    await expect(page.getByText('Заявок нет')).toBeVisible()

    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(
      page,
      '/admin/barber-requests @320',
      test.info()
    )
  })
})

test.describe('axe: 320 px, кабинет', () => {
  test.use({ storageState: storageStatePath('customer') })

  test('профиль: рейл, форма и email без горизонтального скролла', async ({
    page,
  }) => {
    const profile = new AccountProfilePage(page)

    await profile.goto()
    await profile.waitForReady()

    // Длинный email и четыре раздела рейла не должны расширять страницу.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)

    await expectNoBlockingViolations(page, '/account/profile @320', test.info())
  })
})
