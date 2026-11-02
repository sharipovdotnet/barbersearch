import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { RegisterForm } from '@/components/register-form'
import { SessionProvider } from '@/components/session-provider'
import { testUser } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

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

    await user.click(screen.getByRole('checkbox', { name: /Я даю согласие/ }))

    expect(
      screen.queryByText('Отметьте согласие на обработку персональных данных.')
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Политике обработки персональных данных' })
    ).toHaveAttribute('href', '/privacy')
  })

  it('с согласием отправляет регистрацию и входит в созданный аккаунт', async () => {
    const user = userEvent.setup()
    const registerCalls = countRequests('/api/auth/register')

    server.use(
      http.post('/api/auth/register', () =>
        HttpResponse.json(
          { id: '22222222-2222-4222-8222-222222222222' },
          { status: 201 }
        )
      ),
      http.get('/api/auth/me', () => HttpResponse.json(testUser))
    )

    renderForm()
    await fillValidForm(user)
    await user.click(screen.getByRole('checkbox', { name: /Я даю согласие/ }))
    await user.click(screen.getByRole('button', { name: 'Зарегистрироваться' }))

    await waitFor(() => {
      expect(registerCalls()).toBe(1)
    })
  })
})
