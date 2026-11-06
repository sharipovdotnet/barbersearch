import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AdminSummaryPage } from '../pages/admin-summary-page'

/**
 * Админская сводка сама перечитывается, пока вкладка видима.
 *
 * Ждать настоящие тридцать секунд в тесте нельзя, поэтому часы страницы ставятся
 * до навигации и сдвигаются ровно на период опроса. Проверяется реальный запрос
 * к API, а не только наличие таймера: сводка — очередь, и её числа должны
 * совпадать с разделами без ручного «Обновить».
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('админская сводка: автообновление', () => {
  test('через период опроса сама перечитывает сводку', async ({ page }) => {
    await page.clock.install()

    const summary = new AdminSummaryPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      const url = new URL(request.url())

      if (url.pathname === '/api/admin/summary') {
        requests.push(url.search)
      }
    })

    await summary.goto()
    await expect(summary.heading).toBeVisible()

    const initial = requests.length

    await page.clock.fastForward(30_000)

    await expect
      .poll(() => requests.length, { message: 'опрос сходил за сводкой' })
      .toBeGreaterThan(initial)

    // Обновление тихое: блоки и кнопка остаются на месте.
    await expect(summary.reviewsBlock).toBeVisible()
    await expect(summary.updatedAt).toBeVisible()
    await expect(summary.refreshButton).toBeVisible()
  })
})
