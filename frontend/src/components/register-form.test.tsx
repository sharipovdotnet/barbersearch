import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { RegisterForm } from '@/components/register-form'
import { SessionProvider } from '@/components/session-provider'
import { PRIVACY_POLICY_VERSION } from '@/lib/legal'
import { testUser } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

// Литерал, ожидаемый на сервере (PrivacyPolicy.Version): тест падает, если клиент
// отправит другую редакцию, — иначе живые регистрации начали бы получать 400.
const EXPECTED_CONSENT_VERSION = '2026-11-11'

/** Форма живёт внутри настоящей сессии: успех регистрации — это `signIn` из провайдера. */
function renderForm() {
  render(
    <SessionProvider>
      <RegisterForm />
    </SessionProvider>
  )
}

async function fillValidForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Имя'), 'Анна')
  await user.type(screen.getByLabelText('Фамилия'), 'Иванова')
  await user.type(screen.getByLabelText('Email'), 'anna@example.com')
  await user.type(screen.getByLabelText('Пароль'), 'correct-horse-1')
  await user.type(screen.getByLabelText('Повтор пароля'), 'correct-horse-1')
}

beforeEach(() => {
  window.history.replaceState(null, '', '/register')
})

describe('RegisterForm', () => {
  it('без согласия на обработку данных показывает ошибку и не создаёт аккаунт', async () => {
    const user = userEvent.setup()
    const registerCalls = countRequests('/api/auth/register')

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    expect(
      screen.getByText('Отметьте согласие на обработку персональных данных.')
    ).toBeInTheDocument()
    expect(registerCalls()).toBe(0)
  })

  it('согласие снимает ошибку и открывает Политику отдельной ссылкой', async () => {
    const user = userEvent.setup()

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))
    expect(
      screen.getByText('Отметьте согласие на обработку персональных данных.')
    ).toBeInTheDocument()

    await user.click(screen.getByRole('checkbox', { name: /Я согласен/ }))

    expect(
      screen.queryByText('Отметьте согласие на обработку персональных данных.')
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Политике обработки персональных данных' })
    ).toHaveAttribute('href', '/privacy')
  })

  it('называет редакцию Политики в отметке согласия', () => {
    renderForm()

    expect(screen.getByText(/редакция от/)).toBeInTheDocument()
    expect(
      screen.getByText(
        new RegExp(
          `редакция от ${new Intl.DateTimeFormat('ru-RU', {
            day: 'numeric',
            month: 'long',
            year: 'numeric',
          }).format(new Date(`${EXPECTED_CONSENT_VERSION}T00:00:00Z`))}`
        )
      )
    ).toBeInTheDocument()
  })

  it('отправляет редакцию Политики вместе с регистрацией', async () => {
    const user = userEvent.setup()
    let requestBody: unknown = null

    server.use(
      http.post('/api/auth/register', async ({ request }) => {
        requestBody = await request.json()

        return HttpResponse.json(
          { id: '22222222-2222-4222-8222-222222222222' },
          { status: 201 }
        )
      }),
      http.get('/api/auth/me', () => HttpResponse.json(testUser))
    )

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('checkbox', { name: /Я согласен/ }))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() => {
      expect(requestBody).not.toBeNull()
    })

    expect(requestBody).toMatchObject({
      email: 'anna@example.com',
      consentVersion: PRIVACY_POLICY_VERSION,
    })
    expect(PRIVACY_POLICY_VERSION).toBe(EXPECTED_CONSENT_VERSION)
  })

  it('устаревшая редакция Политики показывается общей ошибкой и не создаёт аккаунт', async () => {
    const user = userEvent.setup()
    const registerCalls = countRequests('/api/auth/register')

    server.use(
      http.post('/api/auth/register', () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            status: 400,
            errorCodes: ['auth.register.consentVersion'],
          },
          { status: 400 }
        )
      )
    )

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('checkbox', { name: /Я согласен/ }))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Политика обработки данных обновилась. Обновите страницу и подтвердите согласие снова.'
    )
    expect(registerCalls()).toBe(1)
    expect(
      screen.getByRole('button', { name: 'Зарегистрироваться' })
    ).toBeEnabled()
  })

  it('серверный отказ «согласие не передано» показывается у отметки', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/auth/register', () =>
        HttpResponse.json(
          {
            title: 'Bad Request',
            status: 400,
            errorCodes: ['auth.register.consentRequired'],
          },
          { status: 400 }
        )
      )
    )

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('checkbox', { name: /Я согласен/ }))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    expect(
      await screen.findByText('Отметьте согласие на обработку персональных данных.')
    ).toBeInTheDocument()
  })

  it('после 201 и авто-входа ведёт в безопасность с отметкой об отправке письма', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/auth/register', () =>
        HttpResponse.json(
          { id: '33333333-3333-4333-8333-333333333333' },
          { status: 201 }
        )
      ),
      http.get('/api/auth/me', () => HttpResponse.json(testUser))
    )

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('checkbox', { name: /Я согласен/ }))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() =>
      expect(window.location.pathname).toBe('/account/security')
    )
    expect(window.location.search).toBe('?verification=sent')
  })
})
