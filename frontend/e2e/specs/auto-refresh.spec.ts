import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * Кабинет барбера сам перечитывает «Ближайшие».
 *
 * Ждать настоящие тридцать секунд в тесте нельзя, поэтому часы страницы ставятся
 * до навигации и сдвигаются ровно на период опроса. Проверяется реальный запрос к
 * API и подпись «Обновлено в …», а не только факт наличия таймера.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('кабинет барбера: автообновление', () => {
  test('через период опроса сам перечитывает «Ближайшие»', async ({ page }) => {
    await page.clock.install()

    const cabinet = new CabinetPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      const url = new URL(request.url())

      if (url.pathname === '/api/barber/bookings') {
        requests.push(url.search)
      }
    })

    await cabinet.goto()
    await cabinet.waitForUpcoming()

    const initial = requests.length

    await page.clock.fastForward(30_000)

    await expect
      .poll(() => requests.length, { message: 'опрос сходил за записями' })
      .toBeGreaterThan(initial)
    expect(requests.at(-1)).toContain('scope=upcoming')

    // Обновление тихое: строки не уходят в скелетон и не теряются.
    await expect(cabinet.rows).toHaveCount(20)
    await expect(page.getByText(/Обновлено в \d{2}:\d{2}/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Обновить' })).toBeVisible()
  })
})
