import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * Кабинет барбера сам перечитывает открытый вид.
 *
 * Ждать настоящие тридцать секунд в тесте нельзя, поэтому часы страницы ставятся
 * до навигации и сдвигаются ровно на период опроса. Проверяется реальный запрос к
 * API и подпись «Обновлено в …», а не только факт наличия таймера.
 *
 * Кабинет открывается на «Сегодня», а лента дня собирается из двух списков: тик
 * перечитывает оба (`scope=upcoming` и `scope=past`), а не только «Ближайшие».
 * Стартовое число запросов не проверяется точным: `StrictMode` в dev отправляет
 * второй, сразу отменённый запрос.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('кабинет барбера: автообновление', () => {
  test('через период опроса сам перечитывает ленту дня', async ({ page }) => {
    await page.clock.install()

    const cabinet = new CabinetPage(page)
    const requests: string[] = []
    const count = (scope: string) =>
      requests.filter((search) => search.includes(`scope=${scope}`)).length

    page.on('request', (request) => {
      const url = new URL(request.url())

      if (url.pathname === '/api/barber/bookings') {
        requests.push(url.search)
      }
    })

    await cabinet.goto()
    await expect(cabinet.dayHeading).toBeVisible()

    // Лента дня готова к опросу: оба контроллера уже отдали первые страницы.
    await expect(cabinet.panel).toHaveAttribute('aria-busy', 'false')

    const upcomingBefore = count('upcoming')
    const pastBefore = count('past')

    expect(upcomingBefore, '«Ближайшие» загрузились на монтировании').toBeGreaterThan(0)
    expect(pastBefore, '«История» загрузилась на монтировании').toBeGreaterThan(0)

    await page.clock.fastForward(30_000)

    await expect
      .poll(() => count('upcoming'), {
        message: 'опрос сходил за «Ближайшими»',
      })
      .toBe(upcomingBefore + 1)
    await expect
      .poll(() => count('past'), {
        message: 'опрос сходил за «Историей»',
      })
      .toBe(pastBefore + 1)

    // Обновление тихое: подпись и кнопка на месте, панель не уходит в скелетон.
    await expect(page.getByText(/Обновлено в \d{2}:\d{2}/)).toBeVisible()
    await expect(page.getByRole('button', { name: 'Обновить' })).toBeVisible()
    await expect(cabinet.panel).toHaveAttribute('aria-busy', 'false')
  })
})
