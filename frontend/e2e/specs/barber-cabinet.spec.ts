import { expect, test } from '../fixtures/test'

import { accounts, storageStatePath } from '../fixtures/accounts'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber` — вкладки и пагинация (§4.4).
 *
 * Данные: 21 предстоящая запись (вторая страница — одна строка) и 52 записи
 * в истории. Мутации — в отдельном файле.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('кабинет барбера', () => {
  test('вкладки: переключение не делает запросов, «Сегодня» возвращает списки к началу', async ({
    page,
  }) => {
    const cabinet = new CabinetPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/barber/bookings') {
        requests.push(request.url())
      }
    })

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await cabinet.pager.expectRange('1–20')

    await cabinet.pager.clickNext()
    await expect(cabinet.pager.range).not.toHaveText('1–20')

    // «История» — свой контроллер: первая страница и другой заголовок.
    let before = requests.length
    await cabinet.openTab('История')
    await expect(cabinet.listHeading).toHaveText('История записей')
    await expect(cabinet.rows).toHaveCount(20)
    await cabinet.pager.expectRange('1–20')
    expect(requests.length - before, 'вкладки не ходят в сеть').toBe(0)

    // Возврат в «Ближайшие»: страница и кэш на месте, запроса тоже нет.
    before = requests.length
    await cabinet.openTab('Ближайшие')
    await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
    const bounds = await cabinet.pager.expectRangeFrom(21)

    expect(bounds.from).toBe(21)
    expect(requests.length - before, 'вкладки не ходят в сеть').toBe(0)

    // Лента дня читает первые страницы, поэтому сбрасывает номер страницы.
    before = requests.length
    await cabinet.openTab('Сегодня')
    await expect(cabinet.dayHeading).toBeVisible()
    await cabinet.openTab('Ближайшие')
    await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
    await cabinet.pager.expectRange('1–20')
    expect(requests.length - before, 'переключение вкладок не делает запросов').toBe(0)

    // «Дальше» берёт уже загруженную страницу из кэша — без запроса.
    before = requests.length
    await cabinet.pager.clickNext()
    await expect(cabinet.pager.range).not.toHaveText('1–20')
    expect(requests.length - before, 'кэш страницы переиспользуется').toBe(0)
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

  test('диалог «Записать клиента»: находит клиента и показывает услуги', async ({
    page,
  }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await cabinet.waitForUpcoming()
    await cabinet.openBookingDialog()

    const dialog = cabinet.bookingDialog

    await expect(
      dialog.getByRole('heading', { name: 'Записать клиента' })
    ).toBeVisible()

    await dialog
      .getByRole('searchbox', { name: 'Клиент' })
      .fill('e2e-customer@barbersearch.test')

    await expect(
      dialog.getByRole('button', {
        name: /e2e-customer@barbersearch\.test/,
      })
    ).toBeVisible()

    // Роль не ограничена: администратора тоже можно записать.
    await dialog
      .getByRole('searchbox', { name: 'Клиент' })
      .fill(accounts.admin.email)
    await expect(
      dialog.getByRole('button', {
        name: new RegExp(accounts.admin.email.replaceAll('.', '\\.')),
      })
    ).toBeVisible()

    await expect(
      dialog.getByRole('combobox', { name: 'Услуга' })
    ).toBeVisible()

    // Новый клиент без аккаунта: поиск по базе заменяется именем и телефоном.
    // Радиокнопка скрыта визуально (`sr-only`), поэтому кликаем по её подписи.
    await dialog.getByText('Новый клиент', { exact: true }).click()
    await expect(
      dialog.getByRole('radio', { name: 'Новый клиент' })
    ).toBeChecked()
    await expect(dialog.getByLabel(/Имя/)).toBeVisible()
    await expect(dialog.getByLabel(/Телефон/)).toBeVisible()
    await expect(
      dialog.getByRole('searchbox', { name: 'Клиент' })
    ).toHaveCount(0)
  })
})
