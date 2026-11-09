import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { ResetPasswordPage } from '@/pages/reset-password-page'
import type { SessionUser } from '@/lib/auth'
import { problemDetails, testUser } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const CONFIRM_URL = '/api/auth/password-reset/confirm'

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user'],
  signOut: () => Promise<void>
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => testUser as SessionUser,
    signOut,
    retrySession: () => {},
  }
}

function renderPage(
  options: {
    status?: SessionContextValue['status']
    user?: SessionContextValue['user']
    signOut?: () => Promise<void>
  } = {}
) {
  const {
    status = 'anonymous',
    user = null,
    signOut = async () => {},
  } = options

  return render(
    <SessionContext.Provider value={sessionValue(status, user, signOut)}>
      <ResetPasswordPage />
    </SessionContext.Provider>
  )
}

async function fillPasswords(
  user: ReturnType<typeof userEvent.setup>,
  password: string,
  repeat = password
) {
  await user.type(screen.getByLabelText('Новый пароль'), password)
  await user.type(screen.getByLabelText('Повторите пароль'), repeat)
}

async function submit(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    screen.getByRole('button', { name: 'Сохранить новый пароль' })
  )
}

beforeEach(() => {
  window.history.replaceState(null, '', '/reset-password?token=token-123')
})

describe('ResetPasswordPage: токен из адреса', () => {
  it('без токена сразу показывает «Ссылка неполная» и не делает запрос', () => {
    window.history.replaceState(null, '', '/reset-password')
    const calls = countRequests(CONFIRM_URL)

    renderPage()

    expect(
      screen.getByRole('heading', { name: 'Ссылка неполная' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Запросить новую ссылку' })
    ).toHaveAttribute('href', '/forgot-password')
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )
    expect(calls()).toBe(0)
  })

  it('пробельный токен считается отсутствующим', () => {
    window.history.replaceState(null, '', '/reset-password?token=%20%20')
    const calls = countRequests(CONFIRM_URL)

    renderPage()

    expect(
      screen.getByRole('heading', { name: 'Ссылка неполная' })
    ).toBeInTheDocument()
    expect(calls()).toBe(0)
  })

  it('с токеном показывает форму и сразу очищает адрес', async () => {
    renderPage()

    expect(
      screen.getByRole('heading', { name: 'Новый пароль' })
    ).toBeInTheDocument()

    await waitFor(() => expect(window.location.search).toBe(''))
    expect(window.location.pathname).toBe('/reset-password')
  })
})

describe('ResetPasswordPage: клиентские проверки', () => {
  it('пустая форма показывает обязательность без POST', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CONFIRM_URL)

    renderPage()
    await submit(user)

    expect(screen.getByText('Введите новый пароль.')).toBeInTheDocument()
    expect(calls()).toBe(0)
  })

  it('короткий пароль не отправляется', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CONFIRM_URL)

    renderPage()
    await fillPasswords(user, 'short')
    await submit(user)

    expect(screen.getByText('Минимум 12 символов.')).toBeInTheDocument()
    expect(calls()).toBe(0)
  })

  it('слишком длинный пароль не отправляется', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CONFIRM_URL)
    const longPassword = 'a'.repeat(129)

    renderPage()
    await fillPasswords(user, longPassword)
    await submit(user)

    expect(screen.getByText('Максимум 128 символов.')).toBeInTheDocument()
    expect(calls()).toBe(0)
  })

  it('несовпадение паролей не отправляется', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CONFIRM_URL)

    renderPage()
    await fillPasswords(user, 'new-secret-password', 'other-secret-password')
    await submit(user)

    expect(screen.getByText('Пароли не совпадают.')).toBeInTheDocument()
    expect(calls()).toBe(0)
  })
})

describe('ResetPasswordPage: успех', () => {
  it('после 204 показывает «Пароль изменён», вход и ссылку домой', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CONFIRM_URL)
    let payload: unknown = null

    server.use(
      http.post(CONFIRM_URL, async ({ request }) => {
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    expect(
      await screen.findByRole('heading', { name: 'Пароль изменён' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Войдите с новым паролем. Все сессии входа завершены.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )
    expect(calls()).toBe(1)
    expect(payload).toEqual({
      token: 'token-123',
      newPassword: 'new-secret-password',
    })
  })

  it('при вошедшей сессии завершает её, при анонимной — не зовёт signOut', async () => {
    const user = userEvent.setup()
    const signedInSignOut = vi.fn(async () => {})
    const anonymousSignOut = vi.fn(async () => {})

    server.use(http.post(CONFIRM_URL, () => new HttpResponse(null, { status: 204 })))

    const first = renderPage({
      status: 'authenticated',
      user: testUser,
      signOut: signedInSignOut,
    })

    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    await screen.findByRole('heading', { name: 'Пароль изменён' })
    await waitFor(() => expect(signedInSignOut).toHaveBeenCalledTimes(1))

    first.unmount()

    // Страница очистила адрес; для второго сценария токен нужен снова.
    window.history.replaceState(null, '', '/reset-password?token=token-123')
    renderPage({ status: 'anonymous', signOut: anonymousSignOut })
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    await screen.findByRole('heading', { name: 'Пароль изменён' })
    expect(anonymousSignOut).not.toHaveBeenCalled()
  })

  it('делает ровно один POST на отправку', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CONFIRM_URL)
    let release: (() => void) | undefined
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })

    server.use(
      http.post(CONFIRM_URL, async () => {
        await gate

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    const pending = await screen.findByRole('button', {
      name: 'Сохраняем…',
    })
    expect(pending).toBeDisabled()

    // Повторный клик по заблокированной кнопке ничего не отправляет.
    await user.click(pending)
    expect(calls()).toBe(1)

    release?.()
    await screen.findByRole('heading', { name: 'Пароль изменён' })
    expect(calls()).toBe(1)
  })
})

describe('ResetPasswordPage: серверные ошибки', () => {
  it('400 invalid заменяет форму карточкой недействительной ссылки', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(CONFIRM_URL, () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['auth.passwordReset.invalid'],
          }),
          { status: 400 }
        )
      )
    )

    renderPage()
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    expect(
      await screen.findByRole('heading', { name: 'Ссылка недействительна' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Ссылка для сброса пароля устарела или уже использована. Запросите новую.'
      )
    ).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Новый пароль')
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: 'Запросить новую ссылку' })
    ).toHaveAttribute('href', '/forgot-password')
  })

  it.each([
    ['auth.password.required', 'Введите новый пароль.'],
    ['auth.password.tooShort', 'Минимум 12 символов.'],
    ['auth.password.tooLong', 'Максимум 128 символов.'],
    ['auth.password.common', 'Этот пароль слишком распространён.'],
    ['auth.password.equalsEmail', 'Пароль не должен совпадать с email.'],
    [
      'auth.password.equalsName',
      'Пароль не должен совпадать с именем или фамилией.',
    ],
  ])(
    'код %s становится ошибкой поля, и форма остаётся',
    async (code, message) => {
      const user = userEvent.setup()

      server.use(
        http.post(CONFIRM_URL, () =>
          HttpResponse.json(
            problemDetails(400, { errorCodes: [code] }),
            { status: 400 }
          )
        )
      )

      renderPage()
      await fillPasswords(user, 'new-secret-password')
      await submit(user)

      expect(await screen.findByText(message)).toBeInTheDocument()
      expect(screen.getByLabelText('Новый пароль')).toBeInTheDocument()
      expect(
        screen.queryByRole('heading', { name: 'Ссылка недействительна' })
      ).not.toBeInTheDocument()
      expect(
        screen.queryByRole('heading', { name: 'Пароль изменён' })
      ).not.toBeInTheDocument()
    }
  )

  it('429 показывает текст про лимит и оставляет форму', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(CONFIRM_URL, () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    renderPage()
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Слишком много попыток. Подождите минуту и повторите.'
    )
    expect(screen.getByLabelText('Новый пароль')).toBeInTheDocument()
  })

  it('сеть показывает текст про отсутствие связи и оставляет форму', async () => {
    const user = userEvent.setup()

    server.use(http.post(CONFIRM_URL, () => HttpResponse.error()))

    renderPage()
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
    expect(screen.getByLabelText('Новый пароль')).toBeInTheDocument()
  })

  it('5xx показывает запасной текст и оставляет форму', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(CONFIRM_URL, () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderPage()
    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Не удалось сохранить пароль. Попробуйте позже.'
    )
    expect(screen.getByLabelText('Новый пароль')).toBeInTheDocument()
  })
})

describe('ResetPasswordPage: документ и доступность', () => {
  it('ставит заголовок «Новый пароль — BarberSearch» и фокусирует заголовок успеха', async () => {
    const user = userEvent.setup()

    server.use(http.post(CONFIRM_URL, () => new HttpResponse(null, { status: 204 })))

    renderPage()

    expect(document.title).toBe('Новый пароль — BarberSearch')

    await fillPasswords(user, 'new-secret-password')
    await submit(user)

    const heading = await screen.findByRole('heading', {
      name: 'Пароль изменён',
    })

    await waitFor(() => expect(heading).toHaveFocus())
  })
})
