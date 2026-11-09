import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { ForgotPasswordPage } from '@/pages/forgot-password-page'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const REQUEST_URL = '/api/auth/password-reset'

beforeEach(() => {
  window.history.replaceState(null, '', '/forgot-password')
})

function renderPage() {
  return render(<ForgotPasswordPage />)
}

async function submitEmail(
  user: ReturnType<typeof userEvent.setup>,
  email: string
) {
  await user.type(screen.getByLabelText('Email'), email)
  await user.click(screen.getByRole('button', { name: 'Отправить ссылку' }))
}

describe('ForgotPasswordPage: клиентские проверки', () => {
  it('пустой email показывает ошибку и не отправляет запрос', async () => {
    const user = userEvent.setup()
    const calls = countRequests(REQUEST_URL)

    renderPage()

    await user.click(screen.getByRole('button', { name: 'Отправить ссылку' }))

    expect(screen.getByText('Введите email.')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveAttribute(
      'aria-invalid',
      'true'
    )
    expect(calls()).toBe(0)
    expect(
      screen.queryByRole('heading', { name: 'Проверьте почту' })
    ).not.toBeInTheDocument()
  })

  it('кривой email показывает ошибку формата и не отправляет запрос', async () => {
    const user = userEvent.setup()
    const calls = countRequests(REQUEST_URL)

    renderPage()
    await submitEmail(user, 'not-an-email')

    expect(
      screen.getByText('Проверьте email: нужен адрес вида name@example.com.')
    ).toBeInTheDocument()
    expect(calls()).toBe(0)
  })

  it('email длиннее 254 символов не отправляется', async () => {
    const user = userEvent.setup()
    const calls = countRequests(REQUEST_URL)
    const longEmail = `${'a'.repeat(250)}@example.com`

    renderPage()
    await submitEmail(user, longEmail)

    expect(
      screen.getByText('Проверьте email: нужен адрес вида name@example.com.')
    ).toBeInTheDocument()
    expect(calls()).toBe(0)
  })
})

describe('ForgotPasswordPage: успех', () => {
  it('после 204 показывает «Проверьте почту» с адресом, кулдаун и ссылки', async () => {
    const user = userEvent.setup()
    const calls = countRequests(REQUEST_URL)
    let payload: unknown = null

    server.use(
      http.post(REQUEST_URL, async ({ request }) => {
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()
    await submitEmail(user, '  client@barbersearch.test  ')

    expect(
      await screen.findByRole('heading', { name: 'Проверьте почту' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Если аккаунт с адресом client@barbersearch.test существует, мы отправили на него ссылку для сброса пароля. Ссылка действует 30 минут.'
      )
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Письма нет? Проверьте папку «Спам» или запросите ссылку ещё раз.'
      )
    ).toBeInTheDocument()

    const resend = screen.getByRole('button', {
      name: /^Повторить через \d+ с$/,
    })
    expect(resend).toBeDisabled()

    expect(
      screen.getByRole('button', { name: 'Указать другой адрес' })
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )
    expect(calls()).toBe(1)
    expect(payload).toEqual({ email: 'client@barbersearch.test' })
  })

  it('анти-энумерация: для несуществующего адреса тот же экран', async () => {
    const user = userEvent.setup()
    let payload: unknown = null

    server.use(
      http.post(REQUEST_URL, async ({ request }) => {
        payload = await request.json()

        // Сервер отвечает `204` и для неизвестного адреса — оракула нет.
        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()
    await submitEmail(user, 'unknown@barbersearch.test')

    expect(
      await screen.findByRole('heading', { name: 'Проверьте почту' })
    ).toBeInTheDocument()
    expect(payload).toEqual({ email: 'unknown@barbersearch.test' })
    expect(screen.queryByText(/не найден/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/не существует/i)).not.toBeInTheDocument()
  })

  it('«Указать другой адрес» возвращает форму', async () => {
    const user = userEvent.setup()

    server.use(http.post(REQUEST_URL, () => new HttpResponse(null, { status: 204 })))

    renderPage()
    await submitEmail(user, 'client@barbersearch.test')
    await screen.findByRole('heading', { name: 'Проверьте почту' })

    await user.click(screen.getByRole('button', { name: 'Указать другой адрес' }))

    expect(
      screen.getByRole('heading', { name: 'Восстановление пароля' })
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Проверьте почту' })
    ).not.toBeInTheDocument()
  })
})

describe('ForgotPasswordPage: ошибки сервера', () => {
  it('409 «почта не настроена» остаётся на форме и объясняет причину', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(REQUEST_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['auth.passwordReset.unavailable'],
          }),
          { status: 409 }
        )
      )
    )

    renderPage()
    await submitEmail(user, 'client@barbersearch.test')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Сброс пароля по почте пока не настроен.'
    )
    expect(
      screen.getByRole('heading', { name: 'Восстановление пароля' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Проверьте почту' })
    ).not.toBeInTheDocument()
  })

  it('429 показывает текст про лимит попыток', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(REQUEST_URL, () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    renderPage()
    await submitEmail(user, 'client@barbersearch.test')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Слишком много попыток. Подождите минуту и повторите.'
    )
  })

  it('сеть показывает текст про отсутствие связи', async () => {
    const user = userEvent.setup()

    server.use(http.post(REQUEST_URL, () => HttpResponse.error()))

    renderPage()
    await submitEmail(user, 'client@barbersearch.test')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Нет связи с сервером. Проверьте подключение и повторите попытку.'
    )
  })

  it('5xx показывает запасной текст и не притворяется успехом', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(REQUEST_URL, () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderPage()
    await submitEmail(user, 'client@barbersearch.test')

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Не удалось отправить ссылку. Попробуйте позже.'
    )
    expect(
      screen.queryByRole('heading', { name: 'Проверьте почту' })
    ).not.toBeInTheDocument()
  })
})

describe('ForgotPasswordPage: документ и доступность', () => {
  it('ставит заголовок вкладки и фокусирует заголовок после смены состояния', async () => {
    const user = userEvent.setup()

    server.use(http.post(REQUEST_URL, () => new HttpResponse(null, { status: 204 })))

    renderPage()

    expect(document.title).toBe('Восстановление пароля — BarberSearch')

    await submitEmail(user, 'client@barbersearch.test')

    const heading = await screen.findByRole('heading', {
      name: 'Проверьте почту',
    })

    await waitFor(() => expect(heading).toHaveFocus())
    expect(document.title).toBe('Восстановление пароля — BarberSearch')
  })
})
