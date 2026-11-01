import { expect, test } from '../fixtures/test'

import { ProfilePage } from '../pages/profile-page'

/**
 * Узкий экран: пагинатор остаётся одной строкой и не растягивает страницу (§4.1).
 *
 * Проект `narrow-320` (320×800) запускает только этот файл, поэтому остальные
 * спеки не гоняются трижды.
 */
test.describe('пагинатор на 320 px', () => {
  test('одна строка, нет горизонтального скролла, кнопки не меньше 44×44', async ({
    page,
  }) => {
    const profile = new ProfilePage(page)
    await profile.goto()
    await profile.waitForFirstPage()

    const viewport = await page.evaluate(() => ({
      innerWidth: window.innerWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }))

    expect(viewport.scrollWidth, 'нет горизонтального скролла').toBe(viewport.innerWidth)

    const previousBox = await profile.pager.previous.boundingBox()
    const nextBox = await profile.pager.next.boundingBox()

    expect(previousBox, 'кнопка «Назад» видна').not.toBeNull()
    expect(nextBox, 'кнопка «Дальше» видна').not.toBeNull()

    // 44×44 — минимальная площадь касания для иконочных кнопок (§11).
    for (const box of [previousBox!, nextBox!]) {
      expect(box.width).toBeGreaterThanOrEqual(44)
      expect(box.height).toBeGreaterThanOrEqual(44)
    }

    // Одна строка: кнопки и диапазон на одной горизонтали.
    expect(Math.abs(previousBox!.y - nextBox!.y), 'кнопки в одной строке').toBeLessThan(2)

    const rangeBox = await profile.pager.range.boundingBox()
    expect(rangeBox).not.toBeNull()
    expect(Math.abs(rangeBox!.y - previousBox!.y)).toBeLessThan(24)
  })
})
