import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { MyReviewsPage } from '../pages/my-reviews-page'

/**
 * `/account/reviews` — чтение и пагинация (§4.3).
 *
 * Аккаунт только для чтения: 12 отзывов дают вторую страницу. Мутации живут
 * в отдельном файле и в отдельном аккаунте, чтобы не менять счётчики здесь.
 */
test.use({ storageState: storageStatePath('customer') })

test.describe('мои отзывы: список', () => {
  test('10 строк, «Дальше» → «11–12», «Назад» без запросов', async ({ page }) => {
    const myReviews = new MyReviewsPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/reviews/mine') {
        requests.push(request.url())
      }
    })

    await myReviews.goto()
    await myReviews.waitForFirstPage()
    await expect(myReviews.heading).toBeVisible()

    await myReviews.pager.expectRange('1–10')

    await myReviews.pager.clickNext()
    await myReviews.pager.expectRange('11–12')
    await expect(myReviews.rows).toHaveCount(2)

    const beforeBack = requests.length
    await myReviews.pager.clickPrevious()
    await myReviews.pager.expectRange('1–10')
    await expect(myReviews.rows).toHaveCount(10)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
  })

  test('ошибка следующей страницы: строки остаются, «Дальше» становится повтором', async ({
    page,
  }) => {
    const myReviews = new MyReviewsPage(page)

    await myReviews.goto()
    await myReviews.waitForFirstPage()

    await page.route(
      (url) => url.pathname === '/api/reviews/mine' && url.searchParams.has('cursor'),
      (route) => route.abort()
    )

    await myReviews.pager.clickNext()

    await expect(myReviews.pager.alert).toBeVisible()
    await expect(myReviews.rows).toHaveCount(10)
    await expect(myReviews.pager.retry).toBeEnabled()
  })

  test('401 на списке показывает гейт входа, а не общий текст ошибки', async ({ page }) => {
    const myReviews = new MyReviewsPage(page)

    await page.route(
      (url) => url.pathname === '/api/reviews/mine',
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

    // Прозрачный refresh тоже отказывает: иначе он вернёт сессию и повторит запрос.
    await page.route(
      (url) => url.pathname === '/api/auth/refresh',
      (route) =>
        route.fulfill({
          status: 401,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            type: 'about:blank',
            title: 'Unauthorized',
            status: 401,
            code: 'auth.refresh.expired',
          }),
        })
    )

    await myReviews.goto()

    // Гейт входа: приложение предлагает войти, а не показывает ошибку списка.
    await expect(page.getByText('Войдите, чтобы увидеть свои отзывы')).toBeVisible()
    await expect(page.getByText('Не удалось загрузить отзывы')).toHaveCount(0)
    await expect(myReviews.rows).toHaveCount(0)
  })
})
