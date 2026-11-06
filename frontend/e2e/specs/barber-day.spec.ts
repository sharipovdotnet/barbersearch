import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { expectNoBlockingViolations } from '../fixtures/axe'
import { CabinetPage } from '../pages/cabinet-page'

/**
 * `/barber` — лента дня: вид по умолчанию и переключение трёх вкладок (§9.5).
 *
 * Сценарий только читает данные: кабинет открывается на «Сегодня», все три
 * вкладки работают, а переключение между ними не делает ни одного запроса —
 * оба списка живут на экране с момента монтирования. Сцена с записями и действиями
 * лежит в `barber-day-mutations.spec.ts`: фикстура «уже начавшегося визита» у одного
 * барбера может быть только одна, поэтому мутирующая часть идёт один раз, последней.
 */
test.use({ storageState: storageStatePath('barber') })

test.describe('кабинет барбера: лента дня', () => {
  test('открывается на «Сегодня», вкладки не перезапрашивают записи', async ({
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

    // 1. Вид по умолчанию — «Сегодня» (D1), вкладок ровно три.
    await expect(cabinet.tab('Сегодня')).toHaveAttribute('aria-selected', 'true')
    await expect(cabinet.tab('Ближайшие')).toHaveAttribute(
      'aria-selected',
      'false'
    )
    await expect(cabinet.tablist.getByRole('tab')).toHaveCount(3)
    await expect(cabinet.dayHeading).toBeVisible()

    // Оба списка загружены на монтировании: панель готова к переключению.
    await expect(cabinet.panel).toHaveAttribute('aria-busy', 'false')

    const before = requests.length

    // 2. «Ближайшие» — серверный список, первый документ уже в памяти.
    await cabinet.openTab('Ближайшие')

    await expect(cabinet.listHeading).toHaveText('Ближайшие записи')
    await expect(cabinet.panel).toHaveAccessibleName('Ближайшие')
    await expect(cabinet.tab('Ближайшие')).toHaveAttribute(
      'aria-selected',
      'true'
    )

    // 3. «История» — второй скоуп, он тоже загрузился на монтировании.
    await cabinet.openTab('История')

    await expect(cabinet.listHeading).toHaveText('История записей')
    await expect(cabinet.panel).toHaveAccessibleName('История')

    // 4. Возврат в «Сегодня» не делает запросов: данные уже загружены.
    await cabinet.openTab('Сегодня')

    await expect(cabinet.dayHeading).toBeVisible()
    await expect(cabinet.panel).toHaveAccessibleName('Сегодня')

    expect(
      requests.length,
      'переключение вкладок не перезапрашивает записи'
    ).toBe(before)

    // 5. Ширина и axe: панель меняется внутри одного `tabpanel`.
    const overflow = await page.evaluate(
      () =>
        document.documentElement.scrollWidth -
        document.documentElement.clientWidth
    )

    expect(overflow).toBeLessThanOrEqual(0)
    await expectNoBlockingViolations(page, '/barber (сегодня)', test.info())
  })

  test('клавиатура: стрелки, Home и End ходят по вкладкам', async ({ page }) => {
    const cabinet = new CabinetPage(page)

    await cabinet.goto()
    await expect(cabinet.dayHeading).toBeVisible()

    await cabinet.tab('Сегодня').focus()

    await page.keyboard.press('ArrowRight')
    await expect(cabinet.tab('Ближайшие')).toBeFocused()
    await expect(cabinet.tab('Ближайшие')).toHaveAttribute(
      'aria-selected',
      'true'
    )

    await page.keyboard.press('End')
    await expect(cabinet.tab('История')).toBeFocused()
    await expect(cabinet.tab('История')).toHaveAttribute('aria-selected', 'true')

    await page.keyboard.press('Home')
    await expect(cabinet.tab('Сегодня')).toBeFocused()
    await expect(cabinet.tab('Сегодня')).toHaveAttribute(
      'aria-selected',
      'true'
    )

    // Стрелка влево с первой вкладки заворачивается на последнюю.
    await page.keyboard.press('ArrowLeft')
    await expect(cabinet.tab('История')).toBeFocused()
  })
})
