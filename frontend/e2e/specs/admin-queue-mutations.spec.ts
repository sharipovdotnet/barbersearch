import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { hidePublishedReview } from '../fixtures/api'
import { AdminReviewsPage } from '../pages/admin-reviews-page'

/**
 * Фокус после действия в очереди (§4.4).
 *
 * Публикация убирает строку из очереди. Фокус обязан остаться внутри списка
 * (на следующей строке или на заголовке), а не улетать в `<body>`.
 *
 * Спека меняет фикстуры, поэтому живёт в `mutations-chromium` и запускается
 * последней. В `finally` отзыв возвращается в очередь тем же админским API,
 * чтобы следующий прогон видел исходные счётчики (§5 README, «Notes»).
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('очередь отзывов: фокус после действия', () => {
  test('после публикации фокус остаётся в очереди', async ({ page, request }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto()
    await expect(adminReviews.heading).toBeVisible()
    await expect(adminReviews.rows.first()).toBeVisible()
    await expect(page.locator('#review-detail')).toBeVisible()

    const row = adminReviews.rows.first()
    const reviewId = await adminReviews.reviewId(row)

    await row.focus()

    try {
      await page
        .locator('#review-detail')
        .getByRole('button', { name: 'Опубликовать' })
        .click()

      // Сначала действие должно завершиться: объявление появляется после ответа API.
      await expect(page.getByText('Отзыв опубликован')).toBeVisible()

      const focused = await page.evaluate(() => {
        const element = document.activeElement

        return {
          isQueueRow: element?.matches('#reviews-list li > button') ?? false,
          text: element?.textContent?.slice(0, 60) ?? '',
        }
      })

      expect(
        focused.isQueueRow,
        `фокус вернулся в очередь, а не в <body>: «${focused.text}»`
      ).toBe(true)
    } finally {
      await hidePublishedReview(request, reviewId)
    }
  })

  /**
   * Счётчик рейла — без перезагрузки (#93).
   *
   * Публикация убирает отзыв из очереди, значит бейдж «Отзывы» в рейле обязан
   * уменьшиться сам: раздел шлёт `admin:queues-changed`, провайдер делает тихий
   * `refresh()` сводки. Тридцатисекундного тика тест не ждёт — иначе проверялся
   * бы таймер, а не событие. В `finally` отзыв возвращается в очередь, чтобы
   * счётчики следующего прогона остались исходными.
   */
  test('бейдж «Отзывы» уменьшается после публикации без перезагрузки', async ({
    page,
    request,
  }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto()
    await expect(adminReviews.heading).toBeVisible()
    await expect(adminReviews.rows.first()).toBeVisible()

    const badge = adminReviews.reviewsBadge

    await expect(badge).toBeVisible()

    const before = Number(await badge.textContent())

    expect(before).toBeGreaterThan(0)

    const row = adminReviews.rows.first()
    const reviewId = await adminReviews.reviewId(row)

    try {
      await page
        .locator('#review-detail')
        .getByRole('button', { name: 'Опубликовать' })
        .click()

      await expect(page.getByText('Отзыв опубликован')).toBeVisible()

      if (before === 1) {
        // Очередь опустела: «0» не рисуется, бейдж исчезает совсем.
        await expect(badge).toHaveCount(0)
      } else {
        await expect(badge).toHaveText(String(before - 1))
      }

      // Страница та же: на широком экране экран сам открывает следующую строку
      // очереди, поэтому проверяем раздел, а не точный адрес без карточки.
      await expect(adminReviews.heading).toBeVisible()
      await expect(page).toHaveURL(/\/admin\/reviews(?:\/|$)/)
    } finally {
      await hidePublishedReview(request, reviewId)
    }
  })
})
