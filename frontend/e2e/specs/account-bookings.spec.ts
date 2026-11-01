import { expect, test } from '../fixtures/test'

import { pageSize, storageStatePath } from '../fixtures/accounts'
import { AccountBookingsPage } from '../pages/account-bookings-page'

/**
 * `/account` — серверные вкладки и общий пагинатор (§4.5–4.6).
 *
 * Данные: у e2e-customer 21 предстоящая запись и 21 запись истории, то есть по две
 * страницы 20 + одна. Мутации — в `account-bookings-mutations.spec.ts`.
 */
test.use({ storageState: storageStatePath('customer') })

test.describe('мои записи: предстоящие и история', () => {
  test('предстоящие: 20 строк, «Дальше» — одна, «Назад» без запросов', async ({
    page,
  }) => {
    const account = new AccountBookingsPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/bookings') {
        requests.push(request.url())
      }
    })

    await account.goto()
    await account.waitForUpcoming()

    await expect(account.heading).toBeVisible()
    await account.upcomingPager.expectRange('1–20')

    await account.upcomingPager.clickNext()
    const bounds = await account.upcomingPager.expectRangeFrom(21)

    expect(bounds).toStrictEqual({ from: 21, to: 21 })
    await expect(account.rows).toHaveCount(1)
    await expect(account.panel, 'фокус уходит на список').toBeFocused()

    const beforeBack = requests.length
    await account.upcomingPager.clickPrevious()
    await account.upcomingPager.expectRange('1–20')
    await expect(account.rows).toHaveCount(pageSize.account)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
  })

  test('история: 20 строк, «Дальше» — одна, «Назад» из кэша', async ({ page }) => {
    const account = new AccountBookingsPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/bookings') {
        requests.push(request.url())
      }
    })

    await account.goto()
    await account.waitForUpcoming()

    await account.openTab('История')
    await expect(account.tab('История')).toHaveAttribute('aria-selected', 'true')
    await expect(account.rows).toHaveCount(pageSize.account)
    await account.historyPager.expectRange('1–20')

    await account.historyPager.clickNext()
    const bounds = await account.historyPager.expectRangeFrom(21)

    expect(bounds).toStrictEqual({ from: 21, to: 21 })
    await expect(account.rows).toHaveCount(1)

    const beforeBack = requests.length
    await account.historyPager.clickPrevious()
    await account.historyPager.expectRange('1–20')
    await expect(account.rows).toHaveCount(pageSize.account)

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
  })

  test('переключение вкладки грузит первую страницу своего скоупа и не смешивает строки', async ({
    page,
  }) => {
    const account = new AccountBookingsPage(page)
    const requests: URL[] = []

    page.on('request', (request) => {
      const url = new URL(request.url())

      if (url.pathname === '/api/bookings') {
        requests.push(url)
      }
    })

    await account.goto()
    await account.waitForUpcoming()

    const upcomingFirstPages = requests.filter(
      (url) => url.searchParams.get('scope') === 'upcoming'
    )

    expect(
      upcomingFirstPages.some((url) => !url.searchParams.has('cursor')),
      'на входе грузится первая страница «Предстоящих»'
    ).toBe(true)

    await account.openTab('История')
    await expect(account.rows).toHaveCount(pageSize.account)

    const historyFirstPages = requests.filter(
      (url) => url.searchParams.get('scope') === 'past'
    )

    expect(
      historyFirstPages.some((url) => !url.searchParams.has('cursor')),
      'вкладка «История» начинает с первой страницы своего скоупа'
    ).toBe(true)

    // Строки не перепутаны: история показывает терминальные статусы, предстоящие — нет.
    await expect(account.panel.getByText('Неявка')).not.toHaveCount(0)
    await expect(account.panel.getByText('Ждёт подтверждения')).toHaveCount(0)

    await account.openTab('Предстоящие')
    await expect(account.rows).toHaveCount(pageSize.account)
    await expect(account.panel.getByText('Ждёт подтверждения')).not.toHaveCount(0)
    await expect(account.panel.getByText('Неявка')).toHaveCount(0)
  })

  test('«Ближайшая запись» — первая строка предстоящих', async ({ page }) => {
    const account = new AccountBookingsPage(page)

    await account.goto()
    await account.waitForUpcoming()

    await expect(account.hero).toBeVisible()
    await expect(account.hero.getByText('Ближайшая запись')).toBeVisible()

    // n = 1 сидируется на барбера B2 («Барбер Номер 1»), n = 2 — на B3.
    await expect(account.rows.nth(0)).toContainText('Барбер Номер 1')
    await expect(account.rows.nth(1)).toContainText('Барбер Номер 2')

    await expect(account.hero).toContainText('Барбер Номер 1')
    await expect(account.hero).toContainText('12:00')
    await expect(account.hero).not.toContainText('Барбер Номер 2')
  })

  test('на странице нет «Показать ещё» и счётчиков вкладок', async ({ page }) => {
    const account = new AccountBookingsPage(page)

    await account.goto()
    await account.waitForUpcoming()

    await expect(page.getByRole('button', { name: 'Показать ещё' })).toHaveCount(0)
    await expect(account.upcomingPager.root).toBeVisible()

    // Подписей «Предстоящие»/«История» достаточно: общего числа сервер не отдаёт.
    await expect(account.tab('Предстоящие')).toHaveText('Предстоящие')
    await expect(account.tab('История')).toHaveText('История')
  })
})
