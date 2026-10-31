import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber` — вкладки и пагинация (§4.4).
 *
 * Данные: 21 предстоящая запись (вторая страница — одна строка) и 52 записи
 * в истории. Мутации — в отдельном файле.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('кабинет барбера', () => {
  test('вкладки: переключение начинает с первой страницы и чистит кэш', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await cabinet.pager.expectRange('1–20')

    await cabinet.pager.clickNext()
    await expect(cabinet.pager.range).not.toHaveText('1–20')

    // «История» — другая выборка, поэтому первая страница и другой заголовок.
    await cabinet.openTab('История')
    await expect(cabinet.listHeading).toHaveText('История записей')
    await expect(cabinet.rows).toHaveCount(20)
    await expect(cabinet.pager.range).toHaveText('1–20')

    // Возврат на «Ближайшие» — снова первая страница, а не закэшированная вторая.
    await cabinet.openTab('Ближайшие')
    await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
    await expect(cabinet.pager.range).toHaveText('1–20')

    // Кэш очищен: «Дальше» снова идёт в сеть.
    const requests: string[] = []
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/barber/bookings') {
        requests.push(request.url())
      }
    })

    await cabinet.pager.clickNext()
    await expect(cabinet.pager.range).not.toHaveText('1–20')
    expect(requests.some((url) => url.includes('cursor='))).toBe(true)
  })

  test('история: 20 строк, «Дальше» → «21–40», «Назад» без запросов', async ({ page }) => {
    const cabinet = new CabinetPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/barber/bookings') {
        requests.push(request.url())
      }
    })

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await cabinet.openTab('История')

    await expect(cabinet.rows).toHaveCount(20)
    await cabinet.pager.expectRange('1–20')

    await cabinet.pager.clickNext()
    const bounds = await cabinet.pager.expectRangeFrom(21)

    expect(bounds.from).toBe(21)
    expect(bounds.to).toBe(40)
    await expect(cabinet.rows).toHaveCount(20)
    await expect(cabinet.listHeading).toBeFocused()

    const beforeBack = requests.length
    await cabinet.pager.clickPrevious()
    await cabinet.pager.expectRange('1–20')
    await expect(cabinet.rows).toHaveCount(20)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
  })

  test('ошибка следующей страницы: строки остаются, «Дальше» становится повтором', async ({
    page,
  }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await cabinet.openTab('История')
    await expect(cabinet.rows).toHaveCount(20)

    await page.route(
      (url) =>
        url.pathname === '/api/barber/bookings' &&
        url.searchParams.get('scope') === 'past' &&
        url.searchParams.has('cursor'),
      (route) => route.abort()
    )

    await cabinet.pager.clickNext()

    await expect(cabinet.pager.alert).toBeVisible()
    await expect(cabinet.rows, 'история не пропала').toHaveCount(20)
    await expect(cabinet.pager.retry).toBeEnabled()
  })
})
