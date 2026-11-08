import { expect, test } from '../fixtures/test'

/**
 * Переключатель языка на витрине.
 *
 * Сценарий не зависит от данных API: заголовок и кнопка поиска есть всегда,
 * а выбор языка хранится в `localStorage` и должен пережить перезагрузку.
 */
test('переключает витрину на английский и запоминает выбор', async ({ page }) => {
  await page.goto('/')

  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Найди барбера и запишись за минуту',
    })
  ).toBeVisible()
  await expect(page.getByRole('button', { name: 'Найти' })).toBeVisible()

  await page.getByRole('button', { name: /English/ }).click()

  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Find a barber and book in a minute',
    })
  ).toBeVisible()
  await expect(page.getByRole('button', { name: 'Search' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  await expect(page).toHaveTitle('BarberSearch — find barbers and book')
  await expect(
    page.getByRole('group', { name: 'Interface language' })
  ).toBeVisible()

  await page.reload()

  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Find a barber and book in a minute',
    })
  ).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')

  await page.getByRole('button', { name: /Русский/ }).click()

  await expect(
    page.getByRole('heading', {
      level: 1,
      name: 'Найди барбера и запишись за минуту',
    })
  ).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'ru')
})

test.describe('переключатель на 320 px', () => {
  test.use({ viewport: { width: 320, height: 800 } })

  test('не создаёт горизонтальную прокрутку и остаётся доступным', async ({
    page,
  }) => {
    await page.goto('/')

    await expect(page.getByRole('button', { name: /English/ })).toBeVisible()

    const hasHorizontalScroll = await page.evaluate(
      () =>
        document.documentElement.scrollWidth >
        document.documentElement.clientWidth
    )

    expect(hasHorizontalScroll).toBe(false)
  })
})
