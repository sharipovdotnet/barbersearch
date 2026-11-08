import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AdminStatisticsPage } from '../pages/admin-statistics-page'

/**
 * `/admin/statistics` — «Статистика сервиса»: рейл, плитки, период из URL,
 * график с таблицей-дублем, сравнение и тихое обновление.
 *
 * Точные суммы общего сида не проверяются: мутирующие спеки меняют брони.
 * Проверяем структуру ответа (`period.days`), новые запросы после смены периода
 * и отсутствие действий записи — раздел только читает.
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('админка: статистика', () => {
  test('второй пункт рейла ведёт на /admin/statistics', async ({ page }) => {
    await page.goto('/admin/reviews')

    const rail = page.getByRole('navigation', {
      name: 'Разделы администрирования',
    })

    await expect(rail.getByRole('link').nth(1)).toHaveText('Статистика')

    await rail.getByRole('link', { name: 'Статистика' }).click()

    await expect(page).toHaveURL(/\/admin\/statistics\?from=/)
    await expect(
      rail.getByRole('link', { name: 'Статистика' })
    ).toHaveAttribute('aria-current', 'page')

    const statistics = new AdminStatisticsPage(page)

    await expect(statistics.heading).toBeVisible()
    await expect(statistics.subtitle).toBeVisible()
    await expect(statistics.periodLine).toBeVisible()
  })

  test('видит плитки, график, статусы и таблицу-дубль', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()

    await expect(statistics.heading).toBeVisible()
    await expect(statistics.region('Обзор')).toBeVisible()

    const overview = statistics.region('Обзор')

    for (const label of [
      'Завершённые визиты',
      'Оборот',
      'Средний чек',
      'Новые аккаунты',
      'Неявки',
      'Зависшие брони',
    ]) {
      await expect(overview.getByText(label, { exact: true })).toBeVisible()
    }

    await expect(overview.getByText('по завершённым визитам')).toBeVisible()
    await expect(statistics.chart).toHaveCount(1)
    await expect(statistics.chart).toHaveAttribute(
      'aria-label',
      /Записи и визиты/
    )
    await expect(page.getByText('Статусы визитов')).toBeVisible()
    await expect(page.getByText(/Закрыто визитов:/)).toBeVisible()

    await statistics.tableToggle.click()

    await expect(statistics.table).toBeVisible()
    await expect(statistics.table.locator('tbody tr').first()).toBeVisible()

    await expect(statistics.region('Лидеры')).toBeVisible()
    await expect(page.getByText('Как считаем')).toBeVisible()
  })

  test('пресет меняет период и делает новый запрос', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()

    const [response] = await Promise.all([
      statistics.waitForResponse(),
      statistics.preset('7 дней').click(),
    ])

    const body = (await response.json()) as { period: { days: number } }

    expect(body.period.days, 'пресет — ровно 7 дней').toBe(7)
    await expect(page).toHaveURL(/from=/)
    await expect(statistics.preset('7 дней')).toHaveAttribute(
      'aria-pressed',
      'true'
    )
  })

  test('свои даты дают запрос с выбранными границами', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()

    const [response] = await Promise.all([
      statistics.waitForResponse(),
      page.getByLabel('С даты').fill('2026-10-01'),
    ])

    const url = new URL(response.url())

    expect(url.searchParams.get('from')).toBe('2026-10-01')
    await expect(page).toHaveURL(/from=2026-10-01/)
  })

  test('сравнение включает прошлый период в запросе', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()

    await expect(statistics.compareCheckbox).not.toBeChecked()

    const [response] = await Promise.all([
      statistics.waitForResponse(),
      statistics.compareCheckbox.check(),
    ])

    expect(new URL(response.url()).searchParams.get('compare')).toBe('true')
    await expect(page).toHaveURL(/compare=1/)
    await expect(page.getByText(/сравнение с/)).toBeVisible()
  })

  test('«Обновить» — ровно один запрос, секции не стираются', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)
    const requests: string[] = []

    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/admin/statistics') {
        requests.push(request.url())
      }
    })

    await statistics.goto()

    const before = requests.length

    await statistics.refresh()

    expect(requests.length - before, 'одно обновление — один запрос').toBe(1)
    await expect(statistics.region('Обзор')).toBeVisible()
    await expect(statistics.updatedAt).toBeVisible()
  })

  test('раздел только читает: нет действий записи', async ({ page }) => {
    const statistics = new AdminStatisticsPage(page)

    await statistics.goto()

    for (const name of ['Показать ещё', 'Создать', 'Удалить', 'Редактировать']) {
      await expect(
        page.getByRole('button', { name, exact: true })
      ).toHaveCount(0)
    }

    await expect(statistics.refreshButton).toHaveCount(1)
  })
})
