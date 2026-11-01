import { expect, test } from '../fixtures/test'

import { storageStatePath } from '../fixtures/accounts'
import { AdminReviewsPage } from '../pages/admin-reviews-page'

/**
 * Клавиатура очереди отзывов (§4.4).
 *
 * Roving tabindex уже реализован: в очереди ровно одна строка с `tabIndex=0`,
 * стрелки/Home/End двигают и выбор, и фокус, Enter и Пробел открывают деталь.
 * Спека доказывает именно это, а не переписывает поведение.
 */
test.use({ storageState: storageStatePath('admin') })

test.describe('очередь отзывов: клавиатура', () => {
  test('Tab доводит до строки, стрелки, Home и End двигают roving-фокус', async ({
    page,
  }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto()
    await expect(adminReviews.heading).toBeVisible()
    await expect(adminReviews.rows.first()).toBeVisible()

    // Настоящий обход по Tab: фокус не выставляется программно.
    let reached = false

    for (let i = 0; i < 80; i += 1) {
      await page.keyboard.press('Tab')

      const onRow = await page.evaluate(
        () =>
          document.activeElement?.matches('#reviews-list li > button') ?? false
      )

      if (onRow) {
        reached = true
        break
      }
    }

    expect(reached, 'Tab доводит до строки очереди').toBe(true)
    await expect(adminReviews.rows.first()).toBeFocused()
    await expect(
      page.locator('section#reviews-list li > button[tabindex="0"]')
    ).toHaveCount(1)

    const focusVisible = await adminReviews.rows
      .first()
      .evaluate((element) => element.matches(':focus-visible'))

    expect(focusVisible, 'на строке видно фокус-кольцо').toBe(true)

    await page.keyboard.press('ArrowDown')
    await expect(adminReviews.rows.nth(1)).toBeFocused()
    await expect(adminReviews.rows.nth(1)).toHaveAttribute(
      'aria-current',
      'true'
    )
    await expect(adminReviews.selectedRow).toHaveCount(1)

    await page.keyboard.press('ArrowUp')
    await expect(adminReviews.rows.first()).toBeFocused()

    await page.keyboard.press('End')
    await expect(adminReviews.rows.last()).toBeFocused()
    await expect(
      page.locator('section#reviews-list li > button[tabindex="0"]')
    ).toHaveCount(1)

    await page.keyboard.press('Home')
    await expect(adminReviews.rows.first()).toBeFocused()
  })

  test('Enter и Пробел открывают деталь выбранной строки', async ({ page }) => {
    const adminReviews = new AdminReviewsPage(page)

    await adminReviews.goto()
    await expect(adminReviews.rows.first()).toBeVisible()

    const lastRow = adminReviews.rows.last()
    const lastId = await adminReviews.reviewId(lastRow)

    await lastRow.focus()
    await page.keyboard.press('Enter')

    await expect(page).toHaveURL(new RegExp(`/admin/reviews/${lastId}$`))
    await expect(lastRow).toHaveAttribute('aria-current', 'true')
    await expect(page.locator('#review-detail')).toBeVisible()

    const firstRow = adminReviews.rows.first()
    const firstId = await adminReviews.reviewId(firstRow)

    await firstRow.focus()
    await page.keyboard.press('Space')

    await expect(page).toHaveURL(new RegExp(`/admin/reviews/${firstId}$`))
    await expect(firstRow).toHaveAttribute('aria-current', 'true')
  })
})
