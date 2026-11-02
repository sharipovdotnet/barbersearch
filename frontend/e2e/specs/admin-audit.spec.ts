import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { AdminAuditPage } from '../pages/admin-audit-page'

/**
 * `/admin/audit` — чтение журнала: рейл, фильтры по дате/типу/автору, порядок,
 * вторая страница и раскрываемый diff.
 *
 * Фикстуры (`seed.sql`, секция 7): 21 строка `audit_log` на 2026-09-10 UTC, свежая —
 * запись 21 без автора («Система»), запись 20 меняет роль. Даты в прошлом и фильтр
 * по ним отделяют фикстуры от строк, которые пишут мутирующие спеки «сегодня».
 * Экраны только читают, поэтому восстанавливать после них нечего.
 */
test.use({ storageState: storageStatePath('admin') })

const DAY = fixtures.journal.day
const ROLE_CHANGE = fixtures.journal.roleChangeRowId
const SYSTEM_ROW = fixtures.journal.systemRowId
const OLDEST_ROW = '9e700000-0000-4000-8000-000000000001'

test.describe('админка: журнал', () => {
  test('рейл ведёт в журнал и отмечает пункт текущим', async ({ page }) => {
    await page.goto('/admin/reviews')

    await page.getByRole('link', { name: 'Журнал' }).click()

    await expect(page).toHaveURL(/\/admin\/audit$/)
    await expect(page.getByRole('link', { name: 'Журнал' })).toHaveAttribute(
      'aria-current',
      'page'
    )

    const audit = new AdminAuditPage(page)

    await expect(audit.heading).toBeVisible()
    await expect(audit.subtitle).toBeVisible()
    // Фильтры живут в состоянии: адрес раздела не меняется.
    await expect(page).toHaveURL(/\/admin\/audit$/)
  })

  test('фильтр по дате: «1–20», «Дальше» → «21–21», «Назад» без запроса', async ({
    page,
  }) => {
    const audit = new AdminAuditPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/admin/audit') {
        requests.push(request.url())
      }
    })

    await audit.goto()
    await audit.filterByDate(DAY)
    await audit.waitForFirstPage()
    await audit.pager.expectRange('1–20')

    await audit.pager.clickNext()
    await audit.pager.expectRange('21–21')
    await expect(audit.rows).toHaveCount(fixtures.journal.total - fixtures.journal.pageSize)
    await expect(audit.rows.first()).toHaveAttribute('data-entry-id', OLDEST_ROW)

    const beforeBack = requests.length

    await audit.pager.clickPrevious()
    await audit.pager.expectRange('1–20')
    await expect(audit.rows).toHaveCount(fixtures.journal.pageSize)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
  })

  test('первая строка — самая свежая и «Система», строка 20 раскрывает diff роли', async ({
    page,
  }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    await audit.filterByDate(DAY)
    await audit.waitForFirstPage()

    // Действие приходит как `user.updated`: подпись — по хвосту, а не вся строка.
    await expect(audit.rows.first()).toHaveAttribute('data-entry-id', SYSTEM_ROW)
    await expect(audit.rows.first()).toContainText('Система')
    await expect(audit.rows.first()).toContainText('Пользователь')
    await expect(audit.rows.first()).toContainText('Изменено')

    const toggle = audit.toggle(ROLE_CHANGE)

    await expect(audit.row(ROLE_CHANGE)).toContainText(
      'e2e-admin@barbersearch.test'
    )
    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    // Панель diff'а существует и скрыта: `aria-controls` ведёт на реальный элемент.
    await expect(audit.diff(ROLE_CHANGE)).toBeHidden()

    await audit.expandButton(ROLE_CHANGE).click()

    await expect(toggle).toHaveAttribute('aria-expanded', 'true')
    await expect(toggle).toBeFocused()
    await expect(audit.diff(ROLE_CHANGE)).toBeVisible()
    await expect(audit.diff(ROLE_CHANGE)).toContainText('Роль')
    await expect(audit.diff(ROLE_CHANGE)).toContainText('Клиент → Администратор')

    await audit.collapseButton(ROLE_CHANGE).click()

    await expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await expect(audit.diff(ROLE_CHANGE)).toBeHidden()
  })

  test('фильтр «Отзыв» оставляет только две записи этого типа', async ({ page }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    await audit.filterByDate(DAY)
    await audit.waitForFirstPage()

    await audit.selectEntityType('Отзыв')

    await expect(audit.rows).toHaveCount(2)
    await expect(audit.rows.first()).toContainText('Отзыв')
    await expect(audit.rows.nth(1)).toContainText('Отзыв')
    await expect(audit.list.getByText('Пользователь', { exact: true })).toHaveCount(0)
    await expect(audit.list.getByText('Барбер', { exact: true })).toHaveCount(0)
  })

  test('фильтр автора: системная запись не находится, «%» — литерал', async ({
    page,
  }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    await audit.filterByDate(DAY)
    await audit.waitForFirstPage()

    await audit.actorInput.fill('e2e-admin')

    // 21 строка фикстур, но у системной записи `actor_user_id = NULL`:
    // под фильтром по email она не находится — это ожидаемое следствие.
    // Сначала ждём исчезновения системной строки: до применения фильтра
    // на первой странице тоже двадцать строк, и счётчик сам по себе ничего не докажет.
    await expect(audit.row(SYSTEM_ROW)).toHaveCount(0)
    await expect(audit.rows).toHaveCount(fixtures.journal.total - 1)

    await audit.actorInput.fill('')

    // Без фильтра по автору системная запись снова первая.
    await expect(audit.rows.first()).toHaveAttribute('data-entry-id', SYSTEM_ROW)

    // «%» в фильтре — литерал, а не «найти всё»: экранирование на сервере.
    await audit.actorInput.fill('%')

    await expect(audit.rows).toHaveCount(0)
    await expect(page.getByText('Ничего не найдено')).toBeVisible()
  })

  test('«Сбросить фильтры» возвращает список к первой странице без даты', async ({
    page,
  }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    await audit.filterByDate(DAY)
    await audit.waitForFirstPage()
    await audit.selectEntityType('Отзыв')

    await expect(audit.rows).toHaveCount(2)
    await expect(audit.resetFiltersButton).toBeVisible()

    await audit.resetFiltersButton.click()

    await expect(audit.resetFiltersButton).toHaveCount(0)
    await expect(audit.fromInput).toHaveValue('')
    await expect(audit.toInput).toHaveValue('')
    await expect(audit.entityTypeSelect).toHaveValue('all')
    await audit.pager.expectRange('1–20')
  })

  test('журнал неизменяем: нет «Показать ещё» и действий над записями', async ({
    page,
  }) => {
    const audit = new AdminAuditPage(page)

    await audit.goto()
    await audit.filterByDate(DAY)
    await audit.waitForFirstPage()

    await expect(audit.refreshButton).toBeEnabled()
    await audit.expectReadOnly()
  })

  test('401 на списке показывает гейт входа, а не текст ошибки', async ({ page }) => {
    const audit = new AdminAuditPage(page)

    // Внедрение сбоя: доказательство ветки `401` внутри экрана (разрешённый приём).
    await page.route(
      (url) => url.pathname === '/api/admin/audit',
      (route) =>
        route.fulfill({
          status: 401,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            type: 'about:blank',
            title: 'Unauthorized',
            status: 401,
            detail: 'Сессия истекла (E2E)',
            code: 'auth.session.expired',
          }),
        })
    )

    // Продление сессии не подменяем отказом: оно «удаётся» (204 без новых cookies),
    // поэтому повтор всё равно получает 401 от внедрённого сбоя. Так проверяется
    // именно гейт экрана, а не гейт страницы после анонимной сессии.
    await page.route(
      (url) => url.pathname === '/api/auth/refresh',
      (route) => route.fulfill({ status: 204 })
    )

    await audit.goto()

    await expect(page.getByText('Вход для администраторов')).toBeVisible()
    await expect(audit.list).toHaveCount(0)
    await expect(page.getByText('Не удалось загрузить журнал')).toHaveCount(0)
  })
})
