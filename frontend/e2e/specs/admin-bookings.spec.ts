import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { AdminBookingsPage } from '../pages/admin-bookings-page'

/**
 * `/admin/bookings` — чтение: рейл, поиск, фильтр статуса, пейджер, карточка
 * и отсутствие лишних действий.
 *
 * Фикстуры (`seed.sql`, секция 5.7): у клиента `e2e-bookings@…` 24 брони на
 * 2026-09-12 UTC — две страницы общего пейджера (20 + 4) и все пять статусов.
 * Дата в прошлом фиксирована: порядок строк и доступность неявки не зависят от
 * дня прогона. Мутации — в `admin-bookings-mutations.spec.ts`; здесь только
 * чтение, поэтому восстанавливать после сцен нечего.
 */
test.use({ storageState: storageStatePath('admin') })

const BOOKINGS = fixtures.adminBookings

test.describe('админка: брони', () => {
  test('рейл ведёт на брони и отмечает пункт текущим', async ({ page }) => {
    await page.goto('/admin/reviews')

    await page.getByRole('link', { name: 'Брони' }).click()

    await expect(page).toHaveURL(/\/admin\/bookings$/)
    await expect(page.getByRole('link', { name: 'Брони' })).toHaveAttribute(
      'aria-current',
      'page'
    )

    const adminBookings = new AdminBookingsPage(page)

    await expect(adminBookings.heading).toBeVisible()
    await expect(adminBookings.subtitle).toBeVisible()
    // Фильтры живут в состоянии: адрес раздела не меняется.
    await expect(page).toHaveURL(/\/admin\/bookings$/)
  })

  test('поиск по email: «1–20», «Дальше» → «21–24», «Назад» без запроса', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/admin/bookings') {
        requests.push(request.url())
      }
    })

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.email)

    // Поиск находит 24 брони, но страница вмещает 20: вторая — это 21–24.
    await adminBookings.waitForRows(BOOKINGS.pageSize)
    await adminBookings.pager.expectRange(`1–${BOOKINGS.pageSize}`)

    await adminBookings.pager.clickNext()
    await adminBookings.pager.expectRange(
      `${BOOKINGS.pageSize + 1}–${BOOKINGS.total}`
    )
    await expect(adminBookings.rows).toHaveCount(BOOKINGS.secondPage)

    const beforeBack = requests.length

    await adminBookings.pager.clickPrevious()
    await adminBookings.pager.expectRange(`1–${BOOKINGS.pageSize}`)
    await expect(adminBookings.rows).toHaveCount(BOOKINGS.pageSize)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
  })

  test('первая строка — самая поздняя бронь: «Неявка» и время в поясе салона', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.email)
    await adminBookings.waitForRows(BOOKINGS.pageSize)

    const firstRow = adminBookings.rows.first()

    // NN 24 — самая поздняя (20:30 UTC): порядок «свежие сверху».
    await expect(firstRow.getByRole('link')).toHaveAttribute(
      'href',
      `/admin/bookings/${BOOKINGS.latestId}`
    )
    await expect(firstRow).toContainText('Неявка')

    // 20:30 UTC в Europe/Moscow — 23:30; пояс браузера теста не салонный.
    await expect(firstRow).toContainText('23:30')
    await expect(firstRow).not.toContainText('20:30')
  })

  test('карточка: клиент, барбер, услуга, салон и время; «Назад» возвращает список', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.email)
    await adminBookings.waitForRows(BOOKINGS.pageSize)

    await adminBookings.openBooking(BOOKINGS.latestId)

    await expect(page).toHaveURL(`/admin/bookings/${BOOKINGS.latestId}`)
    // Выбранная строка помечена `aria-current`: список и карточка — один выбор.
    await expect(adminBookings.selectedLink).toHaveCount(1)

    const detail = adminBookings.detail

    await expect(adminBookings.detailHeading).toBeVisible()
    await expect(detail).toContainText(BOOKINGS.email)
    await expect(detail).toContainText('Тимур Барберов')
    await expect(detail).toContainText('E2E Стрижка')
    await expect(detail).toContainText('E2E Studio')
    await expect(detail).toContainText('23:30')
    await expect(detail).toContainText('Время в поясе салона: Europe/Moscow')
    // Номер брони — текстом, без кнопки копирования.
    await expect(detail).toContainText(BOOKINGS.latestId)

    // Кнопка «Назад» в браузере, а не ссылка экрана: адрес списка не теряется.
    await page.goBack()

    await expect(page).toHaveURL(/\/admin\/bookings$/)
    await expect(adminBookings.detailHeading).toHaveCount(0)
    await adminBookings.waitForRows(BOOKINGS.pageSize)
  })

  test('фильтр статуса не сбрасывает поиск: Pending — 18 строк, NoShow — одна', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.email)
    await adminBookings.waitForRows(BOOKINGS.pageSize)

    await adminBookings.selectStatus('Pending', BOOKINGS.email)

    await expect(adminBookings.rows).toHaveCount(BOOKINGS.pending)
    await expect(adminBookings.searchField).toHaveValue(BOOKINGS.email)

    await adminBookings.selectStatus('NoShow', BOOKINGS.email)

    await expect(adminBookings.rows).toHaveCount(BOOKINGS.noShow)
    await expect(adminBookings.rows.first()).toContainText('Неявка')
    // Поиск остался в поле: смена статуса его не стирает.
    await expect(adminBookings.searchField).toHaveValue(BOOKINGS.email)
  })

  test('точный id находит одну бронь, «%» — литерал, а не «найти всё»', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.cancelId)
    await adminBookings.waitForRows(1)
    await expect(adminBookings.rowLink(BOOKINGS.cancelId)).toBeVisible()

    // «%» экранируется на сервере: подстрока email не совпадает ни с одной бронью.
    await adminBookings.searchFor('%')

    await expect(adminBookings.rows).toHaveCount(0)
    await expect(page.getByText('Ничего не найдено')).toBeVisible()
  })

  test('«Сбросить фильтры» возвращает первую страницу, фильтров в URL нет', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.email)
    await adminBookings.waitForRows(BOOKINGS.pageSize)
    await adminBookings.selectStatus('Pending', BOOKINGS.email)

    await expect(adminBookings.rows).toHaveCount(BOOKINGS.pending)
    await expect(adminBookings.resetFiltersButton).toBeVisible()
    // Email — PII: в адресе только раздел, ни поиска, ни статуса.
    await expect(page).toHaveURL(/\/admin\/bookings$/)

    await adminBookings.resetFilters()

    await expect(adminBookings.resetFiltersButton).toHaveCount(0)
    await expect(adminBookings.searchField).toHaveValue('')
    await expect(adminBookings.statusSelect).toHaveValue('all')
    await adminBookings.pager.expectRange(`1–${BOOKINGS.pageSize}`)
    await expect(page).toHaveURL(/\/admin\/bookings$/)
  })

  test('брони только читаются: нет лишних кнопок, счётчиков и действий в строках', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    await adminBookings.goto()
    await adminBookings.searchFor(BOOKINGS.email)
    await adminBookings.waitForRows(BOOKINGS.pageSize)

    await expect(adminBookings.refreshButton).toBeEnabled()
    await adminBookings.expectReadOnly()
  })

  test('401 на списке показывает гейт входа, а не текст ошибки', async ({
    page,
  }) => {
    const adminBookings = new AdminBookingsPage(page)

    // Внедрение сбоя: доказательство ветки `401` внутри экрана (разрешённый приём).
    await page.route('**/api/admin/bookings*', (route) =>
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

    await adminBookings.goto()

    await expect(page.getByText('Вход для администраторов')).toBeVisible()
    await expect(adminBookings.rows).toHaveCount(0)
    await expect(page.getByText('Не удалось загрузить брони')).toHaveCount(0)
  })
})
