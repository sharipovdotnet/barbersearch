import { accounts } from '../fixtures/accounts'
import { expect, test, type Page } from '../fixtures/test'

/**
 * Согласие на обработку персональных данных (#42): клиент отправляет редакцию Политики
 * вместе с регистрацией, а устаревшая редакция показывается русской ошибкой.
 *
 * Успешная регистрация здесь не выполняется: новый аккаунт сдвинул бы счётчики
 * `/admin/users` и сид-фикстуры, а удаления пользователей в API нет. Поэтому первая
 * сцена подставляет занятый email из фикстур: сервер принимает версию и отвечает `409`,
 * а не `400 auth.register.consentVersion`. Это доказывает приём версии end-to-end,
 * не создавая новую строку.
 */

// Литерал совпадает с PRIVACY_POLICY_VERSION на клиенте и PrivacyPolicy.Version на сервере.
const EXPECTED_CONSENT_VERSION = '2026-11-03'

/** Заполняет форму регистрации данными, проходящими клиентские проверки. */
async function fillRegistrationForm(
  page: Page,
  email: string
): Promise<void> {
  await page.getByLabel('Имя').fill('Анна')
  await page.getByLabel('Фамилия').fill('Иванова')
  await page.getByLabel('Email', { exact: true }).fill(email)
  await page.getByLabel('Пароль', { exact: true }).fill('Correct-Horse-1')
  await page.getByLabel('Повтор пароля').fill('Correct-Horse-1')
  await page.getByRole('checkbox', { name: /Я согласен/ }).click()
}

test.describe('регистрация и согласие на обработку ПД', () => {
  test('отправляет редакцию Политики и получает отказ по занятому email', async ({
    page,
  }) => {
    const requestPromise = page.waitForRequest(
      (request) =>
        request.method() === 'POST' && request.url().endsWith('/api/auth/register')
    )
    const responsePromise = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        response.url().endsWith('/api/auth/register')
    )

    await page.goto('/register')
    await fillRegistrationForm(page, accounts.customer.email)
    await page.getByRole('button', { name: 'Зарегистрироваться' }).click()

    const request = await requestPromise
    expect(request.postDataJSON()).toMatchObject({
      email: accounts.customer.email,
      consentVersion: EXPECTED_CONSENT_VERSION,
    })

    // 409, а не 400 consentVersion: сервер принял редакцию и остановился на занятом email.
    const response = await responsePromise
    expect(response.status()).toBe(409)

    await expect(
      page.getByText(
        'Аккаунт с таким email уже есть. Войдите или укажите другой адрес.'
      )
    ).toBeVisible()
    await expect(page.getByRole('button', { name: 'Зарегистрироваться' })).toBeEnabled()
  })

  test('устаревшая редакция Политики показывает русскую ошибку и не пускает в аккаунт', async ({
    page,
  }) => {
    await page.route(
      (url) => url.pathname === '/api/auth/register',
      (route) =>
        route.fulfill({
          status: 400,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            type: 'about:blank',
            title: 'Bad Request',
            status: 400,
            detail: 'Consent version is outdated (E2E)',
            errorCodes: ['auth.register.consentVersion'],
          }),
        })
    )

    await page.goto('/register')
    await fillRegistrationForm(page, 'e2e-consent-outdated@barbersearch.test')
    await page.getByRole('button', { name: 'Зарегистрироваться' }).click()

    await expect(page.getByRole('alert')).toHaveText(
      'Политика обработки данных обновилась. Обновите страницу и подтвердите согласие снова.'
    )
    await expect(page).toHaveURL(/\/register$/)
    await expect(page.getByRole('button', { name: 'Зарегистрироваться' })).toBeEnabled()
  })
})
