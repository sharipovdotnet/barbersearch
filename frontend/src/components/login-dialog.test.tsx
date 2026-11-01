import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it, vi } from 'vitest'

import { LoginDialog } from '@/components/login-dialog'
import { SessionProvider } from '@/components/session-provider'
import {
  noContent,
  problemDetails,
  testUser,
  unauthorized,
} from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/** Диалог живёт внутри настоящей сессии: вход — это `signIn` из `SessionProvider`. */
function renderDialog(
  props: { redirectTo?: string | null; onSignedIn?: () => void } = {}
) {
  render(
    <SessionProvider>
      <LoginDialog redirectTo={null} {...props} />
    </SessionProvider>
  )
}

/** Открытие — кликом по триггеру в шапке, как это делает пользователь. */
async function openDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Войти' }))

  return await screen.findByRole('dialog')
}

function submitButton(dialog: HTMLElement): HTMLElement {
  return within(dialog).getByRole('button', { name: 'Войти' })
}

function loginFailure(status: number, errorCodes: string[]) {
  return HttpResponse.json(
    problemDetails(status, {
      detail: 'Server diagnostic text.',
      errorCodes,
    }),
    { status }
  )
}

describe('LoginDialog', () => {
  it('на пустой сабмит показывает обе ошибки и не делает запросов', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    renderDialog()
    const dialog = await openDialog(user)

    await user.click(submitButton(dialog))

    expect(within(dialog).getByText('Введите email.')).toBeInTheDocument()
    expect(within(dialog).getByText('Введите пароль.')).toBeInTheDocument()
    expect(
      requests.filter((path) => !path.startsWith('/api/auth/me'))
    ).toStrictEqual([])
  })

  it('на кривой email показывает подсказку про опечатку — русская копия', async () => {
    const user = userEvent.setup()

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(within(dialog).getByLabelText('Email'), 'not-an-email')
    await user.type(within(dialog).getByLabelText('Пароль'), 'secret-pass')
    await user.click(submitButton(dialog))

    expect(
      within(dialog).getByText('Похоже, в адресе опечатка.')
    ).toBeInTheDocument()
  })

  it('на пароль длиннее 128 символов показывает верхнюю границу', async () => {
    const user = userEvent.setup()

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    // Вставка вместо 129 нажатий: onChange срабатывает один раз, тест не ждёт ввода.
    await user.click(within(dialog).getByLabelText('Пароль'))
    await user.paste('a'.repeat(129))
    await user.click(submitButton(dialog))

    expect(within(dialog).getByText('Максимум 128 символов.')).toBeInTheDocument()
  })

  it('сбрасывает ошибку поля при вводе', async () => {
    const user = userEvent.setup()

    renderDialog()
    const dialog = await openDialog(user)

    await user.click(submitButton(dialog))
    expect(within(dialog).getByText('Введите email.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Email'), 'c')

    expect(within(dialog).queryByText('Введите email.')).not.toBeInTheDocument()
  })

  it('показывает pending: поля и кнопка disabled, форма aria-busy, спиннер aria-hidden', async () => {
    const user = userEvent.setup()
    let release: (() => void) | undefined
    const gate = new Promise<void>((resolve) => {
      release = resolve
    })

    server.use(
      http.post('/api/auth/login/cookie', async () => {
        await gate
        return noContent()
      }),
      http.get('/api/auth/me', () => HttpResponse.json(testUser))
    )

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    await user.type(within(dialog).getByLabelText('Пароль'), 'secret-pass')
    await user.click(submitButton(dialog))

    const pendingButton = await within(dialog).findByRole('button', {
      name: 'Входим…',
    })

    expect(pendingButton).toBeDisabled()
    expect(within(dialog).getByLabelText('Email')).toBeDisabled()
    expect(within(dialog).getByLabelText('Пароль')).toBeDisabled()
    expect(dialog.querySelector('form')).toHaveAttribute('aria-busy', 'true')
    expect(pendingButton.querySelector('svg')).toHaveAttribute(
      'aria-hidden',
      'true'
    )

    release?.()
    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
  })

  it('на успешный вход закрывает диалог и зовёт onSignedIn один раз', async () => {
    const user = userEvent.setup()
    const onSignedIn = vi.fn()
    const payloads: unknown[] = []
    let signedIn = false

    server.use(
      http.post('/api/auth/login/cookie', async ({ request }) => {
        payloads.push(await request.json())
        signedIn = true
        return noContent()
      }),
      http.get('/api/auth/me', () =>
        signedIn ? HttpResponse.json(testUser) : unauthorized()
      )
    )

    renderDialog({ onSignedIn })
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    await user.type(within(dialog).getByLabelText('Пароль'), 'secret-pass')
    await user.click(submitButton(dialog))

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
    expect(onSignedIn).toHaveBeenCalledTimes(1)
    expect(payloads).toStrictEqual([
      {
        email: 'client@barbersearch.test',
        password: 'secret-pass',
        rememberMe: false,
      },
    ])
  })

  it('на 401 auth.login.invalidCredentials показывает текст про неверные данные', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/auth/login/cookie', () =>
        loginFailure(401, ['auth.login.invalidCredentials'])
      )
    )

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    await user.type(within(dialog).getByLabelText('Пароль'), 'wrong-pass')
    await user.click(submitButton(dialog))

    expect(
      await within(dialog).findByRole('alert')
    ).toHaveTextContent('Неверный email или пароль.')
  })

  it('на 429 показывает текст про лимит попыток — русская копия', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/auth/login/cookie', () =>
        loginFailure(429, ['auth.login.rateLimited'])
      )
    )

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    await user.type(within(dialog).getByLabelText('Пароль'), 'secret-pass')
    await user.click(submitButton(dialog))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Слишком много попыток входа. Подождите минуту и повторите.'
    )
  })

  it('на код auth.login.lockedOut показывает текст про блокировку — русская копия', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/auth/login/cookie', () =>
        loginFailure(401, ['auth.login.lockedOut'])
      )
    )

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    await user.type(within(dialog).getByLabelText('Пароль'), 'secret-pass')
    await user.click(submitButton(dialog))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Слишком много неудачных попыток. Вход временно заблокирован — попробуйте позже.'
    )
  })

  it('на код auth.csrf.invalid показывает текст про устаревшую сессию — русская копия', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/auth/login/cookie', () =>
        loginFailure(400, ['auth.csrf.invalid'])
      )
    )

    renderDialog()
    const dialog = await openDialog(user)

    await user.type(
      within(dialog).getByLabelText('Email'),
      'client@barbersearch.test'
    )
    await user.type(within(dialog).getByLabelText('Пароль'), 'secret-pass')
    await user.click(submitButton(dialog))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Сессия устарела. Закройте окно и попробуйте снова.'
    )
  })

  it('связывает ошибку с полем через aria-invalid и aria-describedby', async () => {
    const user = userEvent.setup()

    renderDialog()
    const dialog = await openDialog(user)

    const email = within(dialog).getByLabelText('Email')
    expect(within(dialog).getByLabelText('Пароль')).toBeInTheDocument()

    await user.click(submitButton(dialog))

    expect(email).toHaveAttribute('aria-invalid', 'true')
    expect(email).toHaveAttribute('aria-describedby', 'login-email-error')
    expect(within(dialog).getByText('Введите email.')).toHaveAttribute(
      'id',
      'login-email-error'
    )
    // Ошибка не выкидывает фокус из диалога: пользователь продолжает ввод.
    expect(dialog.contains(document.activeElement)).toBe(true)
  })
})
