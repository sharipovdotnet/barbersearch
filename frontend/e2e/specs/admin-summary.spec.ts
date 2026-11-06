import { expect, test } from '../fixtures/test'

import { fixtures, storageStatePath } from '../fixtures/accounts'
import { AdminCataloguePage } from '../pages/admin-catalogue-page'
import { AdminReviewsPage } from '../pages/admin-reviews-page'
import { AdminSummaryPage } from '../pages/admin-summary-page'

/**
 * `/admin/summary` — «Что требует внимания»: рейл, счётчики против разделов,
 * самый старый отзыв, чтение без действий и гейт `401`.
 *
 * Числа фикстур (14/1/1) верны только на пересеянной базе: мутирующие спеки правят
 * отзывы, поэтому сводку проверяем в читающих проектах и сравниваем счётчики
 * с самими разделами, а не только с `accounts.ts`.
 */
test.use({ storageState: storageStatePath('admin') })

const { pendingReviews, unverifiedBarbers, inactiveSalons } = fixtures.summary

test.describe('админка: сводка', () => {
  test('«Сводка» — первый пункт рейла и ведёт на /admin/summary', async ({
    page,
  }) => {
    await page.goto('/admin/reviews')

    const rail = page.getByRole('navigation', {
      name: 'Разделы администрирования',
    })

    await expect(rail.getByRole('link').first()).toHaveText('Сводка')

    await rail.getByRole('link', { name: 'Сводка' }).click()

    await expect(page).toHaveURL(/\/admin\/summary$/)
    await expect(rail.getByRole('link', { name: 'Сводка' })).toHaveAttribute(
      'aria-current',
      'page'
    )

    const summary = new AdminSummaryPage(page)

    await expect(summary.heading).toBeVisible()
    await expect(summary.subtitle).toBeVisible()
  })

  test('/admin по-прежнему открывает очередь отзывов, а не сводку', async ({
    page,
  }) => {
    await page.goto('/admin')

    // `/admin` уводит на очередь отзывов и сразу выбирает первую строку — URL
    // получает id. Важно, что это не сводка.
    await expect(page).toHaveURL(/\/admin\/reviews(?:\/|$)/)
    await expect(page.getByRole('heading', { name: 'Отзывы', level: 1 })).toBeVisible()
  })

  test('счётчики совпадают с разделами', async ({ page }) => {
    const summary = new AdminSummaryPage(page)

    await summary.goto()

    await expect(
      summary.reviewsBlock.getByText(`${pendingReviews} отзывов`)
    ).toBeVisible()
    await expect(
      summary.barbersBlock.getByText(`${unverifiedBarbers} профиль`)
    ).toBeVisible()
    await expect(
      summary.salonsBlock.getByText(`${inactiveSalons} салон`)
    ).toBeVisible()

    // Отзывы: вкладка «На модерации» — ровно столько строк, сколько в сводке.
    // На широком экране split-view сам открывает первую карточку и добавляет её id
    // в адрес, поэтому проверяем раздел, а не точный URL без карточки.
    await summary.queueLink.click()

    const reviews = new AdminReviewsPage(page)

    await expect(page).toHaveURL(/\/admin\/reviews(?:\/|$)/)
    await expect(reviews.rows).toHaveCount(pendingReviews)

    // Барберы: очередь «На проверке» открыта по умолчанию и показывает профиль.
    // На узком экране таблица скрыта, а карточки видны, поэтому берём видимую ветку.
    await page.goto('/admin/barbers')

    await expect(
      page
        .getByText('E2E Барбер Без Подтверждения')
        .filter({ visible: true })
        .first()
    ).toBeVisible()

    // Салоны: фильтр «Выключенные» каталога.
    const catalogue = new AdminCataloguePage(page)

    await catalogue.goto('salons')
    await catalogue.selectStatus('Выключенные')

    await expect(
      catalogue.row(fixtures.catalogueLabels.hiddenSalon).first()
    ).toBeVisible()
  })

  test('«самый старый» — первая строка очереди и открывает её карточку', async ({
    page,
  }) => {
    const reviews = new AdminReviewsPage(page)

    await reviews.goto()
    await expect(reviews.rows.first()).toBeVisible()
    await reviews.rows.first().click()
    await expect(page).toHaveURL(/\/admin\/reviews\/[0-9a-f-]+/)

    const queuePath = new URL(page.url()).pathname

    const summary = new AdminSummaryPage(page)

    await summary.goto()

    // Ссылка «Открыть отзыв» ведёт на тот же адрес, что даёт первая строка очереди.
    await expect(summary.oldestReviewLink).toHaveAttribute('href', queuePath)

    await summary.oldestReviewLink.click()

    await expect(page).toHaveURL(queuePath)
    await expect(page.locator('#review-detail')).toBeVisible()

    await page.goBack()

    await expect(page).toHaveURL(/\/admin\/summary$/)
    await expect(summary.heading).toBeVisible()
  })

  test('«Обновить» делает ровно один запрос, блоки не мигают скелетоном', async ({
    page,
  }) => {
    const summary = new AdminSummaryPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/admin/summary') {
        requests.push(request.url())
      }
    })

    await summary.goto()
    await expect(summary.updatedAt).toBeVisible()

    const before = requests.length

    await summary.refresh()

    expect(requests.length - before, 'одно обновление — один запрос').toBe(1)
    await expect(page.getByText('Загружаем сводку…')).toHaveCount(0)
    await expect(summary.reviewsBlock).toBeVisible()
    await expect(summary.refreshButton).toBeEnabled()
  })

  test('сводка только читает: нет графиков и лишних действий', async ({ page }) => {
    const summary = new AdminSummaryPage(page)

    await summary.goto()

    await expect(summary.reviewsBlock).toBeVisible()
    await summary.expectReadOnly()
    await expect(summary.barbersLink).toHaveAttribute('href', '/admin/barbers')
    await expect(summary.salonsLink).toHaveAttribute(
      'href',
      '/admin/catalogue?tab=salons'
    )
    await expect(summary.queueLink).toHaveAttribute('href', '/admin/reviews')
  })

  test('401 на сводке показывает гейт входа, а не ошибку', async ({ page }) => {
    const summary = new AdminSummaryPage(page)

    // Внедрение сбоя: доказательство ветки `401` внутри экрана (разрешённый приём).
    await page.route(
      (url) => url.pathname === '/api/admin/summary',
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

    // Продление сессии не подменяем отказом: оно «удаётся» (204), поэтому повтор
    // всё равно получает 401 от внедрённого сбоя — проверяется гейт экрана.
    await page.route(
      (url) => url.pathname === '/api/auth/refresh',
      (route) => route.fulfill({ status: 204 })
    )

    await page.goto('/admin/summary')

    await expect(page.getByText('Вход для администраторов')).toBeVisible()
    await expect(summary.reviewsBlock).toHaveCount(0)
    await expect(page.getByText('Не удалось загрузить сводку')).toHaveCount(0)
  })
})
