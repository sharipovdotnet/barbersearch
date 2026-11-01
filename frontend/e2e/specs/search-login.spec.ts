import { expect, test } from '../fixtures/test'

import { fixtures, pageSize } from '../fixtures/accounts'
import { LandingPage } from '../pages/landing-page'
import { LoginDialog } from '../pages/login-dialog'

/**
 * Витрина и вход (§4.2).
 *
 * Копия проверок русская и может меняться: тексты вынесены рядом с проверками,
 * чтобы правку было видно в диффе.
 */
test.use({ storageState: { cookies: [], origins: [] } })

test.describe('витрина и вход', () => {
  test('поиск по городу: карточки, счётчик и «Показать ещё» добавляет страницу', async ({
    page,
  }) => {
    const landing = new LandingPage(page)
    await landing.goto()
    await expect(landing.heading).toBeVisible()

    await landing.search(fixtures.city)
    await landing.expectCardCount(pageSize.search)

    // Русская копия счётчика: «Показаны 12 барберов».
    await expect(landing.counter).toHaveText('Показаны 12 барберов')
    await expect(landing.showMore).toBeVisible()

    const firstPage = await landing.cardTexts()
    await landing.showMore.click()

    // «Показать ещё» — осознанный append, а не замена страницы: фиксируем это,
    // чтобы поведение не «починили» пагинатором (§4.2).
    await landing.expectCardCount(13)
    await expect(landing.counter).toHaveText('Показаны 13 барберов')
    await expect(landing.showMore).toBeHidden()

    const appended = await landing.cardTexts()
    expect(appended.slice(0, pageSize.search)).toEqual(firstPage)
    expect(appended).toHaveLength(firstPage.length + 1)
  })

  test('поиск по несуществующему городу показывает пустое состояние', async ({ page }) => {
    const landing = new LandingPage(page)
    await landing.goto()

    await landing.search('Канзас-Сити')
    await expect(page.getByText('В этом городе никого не нашли')).toBeVisible()
    await expect(landing.cards).toHaveCount(0)
  })

  test('пустая отправка: клиентские ошибки, без запроса на сервер', async ({ page }) => {
    const dialog = new LoginDialog(page)
    await page.goto('/')
    await dialog.open()

    await dialog.submit.click()

    await expect(dialog.error('Введите email.')).toBeVisible()
    await expect(dialog.error('Введите пароль.')).toBeVisible()
    await expect(dialog.heading).toBeVisible()
  })

  test('неверные учётные данные: «Неверный email или пароль.»', async ({ page }) => {
    const dialog = new LoginDialog(page)
    await page.goto('/')
    await dialog.open()

    // Неизвестный email: бэкенд отвечает тем же кодом, что и на неверный пароль,
    // поэтому тест не исчерпывает попытки реальной фикстуры (§9.5).
    await dialog.submitWith('unknown-e2e@barbersearch.test', 'e2e-password')

    await expect(dialog.error('Неверный email или пароль.')).toBeVisible()
  })

  test('успешный вход: шапка показывает пользователя, reload сохраняет сессию', async ({
    page,
  }) => {
    const dialog = new LoginDialog(page)
    await page.goto('/')
    await dialog.open()

    await dialog.signInAs('customer')
    await dialog.expectClosed()

    // Шапка аутентифицированного пользователя: «Выйти» есть, анонимного «Войти» нет.
    // Локатор ограничен шапкой: на /account есть и своя кнопка «Выйти».
    const banner = page.getByRole('banner')
    await expect(banner.getByRole('button', { name: 'Выйти' })).toBeVisible()
    await expect(banner.getByRole('button', { name: 'Войти' })).toHaveCount(0)

    await page.reload()

    await expect(banner.getByRole('button', { name: 'Выйти' })).toBeVisible()
  })
})
