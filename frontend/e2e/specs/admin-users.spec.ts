import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AdminUsersPage } from '../pages/admin-users-page'

/** `/admin/users` — список, фильтры и сохранение выбора в URL (§4.5). */
test.use({ storageState: storageStatePath('admin') })

test.describe('админка: пользователи', () => {
  test('20 строк, «Дальше» → «21–N», «Назад» без запросов', async ({ page }) => {
    const adminUsers = new AdminUsersPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/admin/users') {
        requests.push(request.url())
      }
    })

    await adminUsers.goto()
    await adminUsers.waitForFirstPage()
    await expect(adminUsers.pager.range).toHaveText('1–20')

    await adminUsers.pager.clickNext()
    const bounds = await adminUsers.pager.expectRangeFrom(21)

    expect(bounds.from).toBe(21)
    expect(bounds.to).toBeGreaterThan(21)
    await expect(adminUsers.rows).toHaveCount(bounds.to - 20)

    const beforeBack = requests.length
    await adminUsers.pager.clickPrevious()
    await expect(adminUsers.pager.range).toHaveText('1–20')

    expect(requests.length - beforeBack, '«Назад» не ходит в сеть').toBe(0)
    await expect(page.getByRole('button', { name: 'Показать ещё' })).toHaveCount(0)
  })

  test('смена фильтра статуса сбрасывает на первую страницу', async ({ page }) => {
    const adminUsers = new AdminUsersPage(page)

    await adminUsers.goto()
    await adminUsers.waitForFirstPage()
    await adminUsers.pager.clickNext()
    await expect(adminUsers.pager.range).not.toHaveText('1–20')

    // Заблокированных двое: одна страница, пагинатор исчезает (§2.1).
    await adminUsers.selectStatus('Заблокированные')

    await expect(adminUsers.rows).toHaveCount(2)
    await expect(adminUsers.pager.root).toHaveCount(0)
  })

  test('поиск сбрасывает на первую страницу', async ({ page }) => {
    const adminUsers = new AdminUsersPage(page)

    await adminUsers.goto()
    await adminUsers.waitForFirstPage()
    await adminUsers.pager.clickNext()
    await expect(adminUsers.pager.range).not.toHaveText('1–20')

    // Запрос совпадает со всеми фикстурами: страниц по-прежнему две,
    // поэтому сброс виден по диапазону.
    await adminUsers.search.fill('barbersearch.test')

    await expect(adminUsers.pager.range).toHaveText('1–20')
  })

  test('выбранный пользователь остаётся в URL при смене страницы', async ({ page }) => {
    const adminUsers = new AdminUsersPage(page)

    // На узком экране карточка заменяет список: пагинатор недоступен.
    test.skip(
      (page.viewportSize()?.width ?? 0) < 1024,
      'нужен широкий экран: список и карточка видны одновременно'
    )

    await adminUsers.goto()
    await adminUsers.waitForFirstPage()

    await adminUsers.rows.first().click()
    await expect(page).toHaveURL(/\/admin\/users\/[0-9a-f-]+/)
    const selectedUrl = page.url()

    await adminUsers.pager.clickNext()
    const bounds = await adminUsers.pager.expectRangeFrom(21)

    expect(bounds.from).toBe(21)
    expect(page.url(), 'выбор в URL не сбрасывается').toBe(selectedUrl)
  })
})
