import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { ConfirmEmailChangePage } from '@/pages/confirm-email-change-page'
import type { SessionUser } from '@/lib/auth'
import { problemDetails, testUser } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const CONFIRM_URL = '/api/auth/email-change/confirm'

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
    strict?: boolean
  } = {}
) {
  const {
    status = 'anonymous',
    user = null,
    signOut = async () => {},
    strict = false,
  } = options

  const tree = (
    <SessionContext.Provider value={sessionValue(status, user, signOut)}>
      <ConfirmEmailChangePage />
    </SessionContext.Provider>
  )

  return strict ? render(<StrictMode>{tree}</StrictMode>) : render(tree)
}

beforeEach(() => {
  window.history.replaceState(
    null,
    '',
    '/confirm-email-change?token=token-123'
  )
})

describe('ConfirmEmailChangePage: успех', () => {
  it('подтверждает адрес, убирает токен, завершает сессию и фокусирует заголовок', async () => {
    const signOut = vi.fn(async () => {})
    const calls = countRequests(CONFIRM_URL)
    let payload: unknown = null

    server.use(
      http.post(CONFIRM_URL, async ({ request }) => {
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage({ status: 'authenticated', user: testUser, signOut })

    const heading = await screen.findByRole('heading', {
      name: 'Email изменён',
    })

    expect(
      screen.getByText(
        'Новый адрес подтверждён. Все сессии входа завершены — войдите с новым email и прежним паролем.'
      )
    ).toBeVisible()
    expect(screen.getByRole('button', { name: 'Войти' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )
    expect(calls()).toBe(1)
    expect(payload).toEqual({ token: 'token-123' })

    await waitFor(() => expect(window.location.search).toBe(''))
    expect(window.location.pathname).toBe('/confirm-email-change')
    await waitFor(() => expect(signOut).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(heading).toHaveFocus())
  })

  it('для анонимного посетителя не зовёт signOut', async () => {
    const signOut = vi.fn(async () => {})

    renderPage({ status: 'anonymous', signOut })

    await screen.findByRole('heading', { name: 'Email изменён' })
    expect(signOut).not.toHaveBeenCalled()
  })
})

describe('ConfirmEmailChangePage: отказы', () => {
  it('битый токен показывает «Ссылка недействительна» и очищает адрес', async () => {
    const signOut = vi.fn(async () => {})

    server.use(
      http.post(CONFIRM_URL, () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['auth.emailChange.invalid'],
          }),
          { status: 400 }
        )
      )
    )

    renderPage({ status: 'authenticated', user: testUser, signOut })

    expect(
      await screen.findByRole('heading', { name: 'Ссылка недействительна' })
    ).toBeInTheDocument()
    expect(
      screen.getByText('Ссылка недействительна или устарела.')
    ).toBeVisible()
    expect(
      screen.getByText(
        'Ссылка для смены email устарела или уже использована. Запросите новую в профиле.'
      )
    ).toBeVisible()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )
    expect(signOut).not.toHaveBeenCalled()

    await waitFor(() => expect(window.location.search).toBe(''))
  })

  it('сеть показывает понятную ошибку, а не белый экран', async () => {
    server.use(http.post(CONFIRM_URL, () => HttpResponse.error()))

    renderPage()

    expect(
      await screen.findByRole('heading', {
        name: 'Не удалось проверить ссылку',
      })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Нет связи с сервером. Проверьте подключение и повторите попытку.'
      )
    ).toBeVisible()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )

    await waitFor(() => expect(window.location.search).toBe(''))
  })

  it('409 занятого адреса объясняется, а не выдаётся за успех', async () => {
    server.use(
      http.post(CONFIRM_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['user.changeEmail.emailTaken'],
          }),
          { status: 409 }
        )
      )
    )

    renderPage()

    expect(
      await screen.findByRole('heading', {
        name: 'Не удалось проверить ссылку',
      })
    ).toBeInTheDocument()
    expect(
      screen.getByText('Этот адрес уже занят. Укажите другой.')
    ).toBeVisible()
  })
})

describe('ConfirmEmailChangePage: ссылка без токена', () => {
  it('без токена сразу говорит, что ссылка неполная, и не делает запрос', () => {
    window.history.replaceState(null, '', '/confirm-email-change')
    const calls = countRequests(CONFIRM_URL)

    renderPage()

    expect(
      screen.getByRole('heading', { name: 'Ссылка неполная' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'В ссылке нет токена для смены email. Откройте письмо и перейдите по ссылке целиком.'
      )
    ).toBeVisible()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )
    expect(calls()).toBe(0)
  })

  it('пробельный токен считается отсутствующим', () => {
    window.history.replaceState(
      null,
      '',
      '/confirm-email-change?token=%20%20'
    )
    const calls = countRequests(CONFIRM_URL)

    renderPage()

    expect(
      screen.getByRole('heading', { name: 'Ссылка неполная' })
    ).toBeInTheDocument()
    expect(calls()).toBe(0)
  })
})

describe('ConfirmEmailChangePage: StrictMode', () => {
  it('не дублирует запрос подтверждения', async () => {
    const calls = countRequests(CONFIRM_URL)

    renderPage({ strict: true })

    expect(
      await screen.findByRole('heading', { name: 'Email изменён' })
    ).toBeInTheDocument()
    expect(calls()).toBe(1)
  })
})
