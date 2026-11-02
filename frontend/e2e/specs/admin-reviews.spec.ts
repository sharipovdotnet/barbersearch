import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AdminReviewsPage } from '../pages/admin-reviews-page'

/**
 * `/admin/reviews` — очередь модерации (§4.5).
 *
 * Опубликованных отзывов 24: две страницы по 20. Total сервер не отдаёт,
 * поэтому диапазон проверяется без «из N».
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('админка: отзывы', () => {
  test('вкладка «Опубликованные»: страницы «1–20» и «21–N», без «Показать ещё»', async ({
    page,
  }) => {
    const adminReviews = new AdminReviewsPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/admin/reviews') {
        requests.push(request.url())
      }
    })

    await adminReviews.goto()
    await adminReviews.openTab('Опубликованные')

    await adminReviews.waitForFirstPage()
    const firstBounds = await adminReviews.pager.expectRangeFrom(1)

    expect(firstBounds).toEqual({ from: 1, to: 20 })
    await expect(adminReviews.heading).toBeVisible()

    await adminReviews.pager.clickNext()
    const secondBounds = await adminReviews.pager.expectRangeFrom(21)

    expect(secondBounds.from).toBe(21)
    expect(secondBounds.to).toBeGreaterThan(21)
    await expect(adminReviews.rows).toHaveCount(secondBounds.to - 20)

    const beforeBack = requests.length
    await adminReviews.pager.clickPrevious()
    const backBounds = await adminReviews.pager.expectRangeFrom(1)

    expect(backBounds).toEqual({ from: 1, to: 20 })
    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)

    // Регрессия #31: в админских списках нет «Показать ещё».
    await expect(page.getByRole('button', { name: 'Показать ещё' })).toHaveCount(0)
  })

  test('выбранный отзыв остаётся в URL при смене страницы', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    // На узком экране карточка заменяет список: пагинатор недоступен.
    test.skip(
      (page.viewportSize()?.width ?? 0) < 1024,
      'нужен широкий экран: список и карточка видны одновременно'
    )

    await adminReviews.goto('published')
    await adminReviews.waitForFirstPage()

    await adminReviews.rows.first().click()
    await expect(page).toHaveURL(/\/admin\/reviews\/[0-9a-f-]+/)
    const selectedUrl = page.url()

    await adminReviews.pager.clickNext()
    const bounds = await adminReviews.pager.expectRangeFrom(21)

    expect(bounds.from).toBe(21)
    expect(page.url(), 'выбор в URL не сбрасывается').toBe(selectedUrl)
  })

  test('«Обновить» не сбрасывает страницу и не показывает скелетон', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto('published')
    await adminReviews.waitForFirstPage()
    await adminReviews.pager.clickNext()

    const bounds = await adminReviews.pager.expectRangeFrom(21)
    const rowCount = bounds.to - 20

    await expect(adminReviews.rows).toHaveCount(rowCount)
    await adminReviews.refresh.click()

    // Строки остаются на месте — скелетон на время обновления не подменяет список.
    await expect(adminReviews.rows).toHaveCount(rowCount)
    const afterRefresh = await adminReviews.pager.expectRangeFrom(21)

    expect(afterRefresh).toEqual(bounds)
    await expect(page.getByText(/^Обновлено в /)).toBeVisible()
  })

  test('возврат из карточки оставляет ту же страницу', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto('published')
    await adminReviews.waitForFirstPage()
    await adminReviews.pager.clickNext()

    const bounds = await adminReviews.pager.expectRangeFrom(21)

    await adminReviews.rows.first().click()
    await expect(page).toHaveURL(/\/admin\/reviews\/[0-9a-f-]+/)

    await page.goBack()

    const afterBack = await adminReviews.pager.expectRangeFrom(21)
    expect(afterBack).toEqual(bounds)
  })
})
