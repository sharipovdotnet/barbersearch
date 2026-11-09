import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { VerifyEmailPage } from '@/pages/verify-email-page'
import { testUser } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const CONFIRM_URL = '/api/auth/email-verification/confirm'

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user'],
  retrySession: () => void = () => {}
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => testUser,
    signOut: async () => {},
    retrySession,
  }
}

function renderPage(
  status: SessionContextValue['status'] = 'anonymous',
  user: SessionContextValue['user'] = null,
  retrySession: () => void = () => {},
  strict = false
) {
  const tree = (
    <SessionContext.Provider value={sessionValue(status, user, retrySession)}>
      <VerifyEmailPage />
    </SessionContext.Provider>
  )

  return strict ? render(<StrictMode>{tree}</StrictMode>) : render(tree)
}

beforeEach(() => {
  window.history.replaceState(null, '', '/verify-email?token=token-123')
})

describe('VerifyEmailPage', () => {
  it('подтверждает адрес, обновляет сессию и убирает токен из адреса', async () => {
    const retrySession = vi.fn()
    const confirmCalls = countRequests(CONFIRM_URL)
    const refreshCalls = countRequests('/api/auth/refresh')

    renderPage('authenticated', testUser, retrySession)

    expect(
      await screen.findByRole('heading', { name: 'Адрес подтверждён' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Email подтверждён. Теперь на него можно получать письма о записях.'
      )
    ).toBeInTheDocument()
    expect(confirmCalls()).toBe(1)

    await waitFor(() => expect(window.location.search).toBe(''))
    expect(window.location.pathname).toBe('/verify-email')

    await waitFor(() => expect(refreshCalls()).toBe(1))
    await waitFor(() => expect(retrySession).toHaveBeenCalledTimes(1))
    expect(
      screen.getByRole('link', { name: 'Перейти в кабинет' })
    ).toHaveAttribute('href', '/account')
  })

  it('битый или просроченный токен показывает invalid и убирает токен', async () => {
    const retrySession = vi.fn()

    server.use(
      http.post(CONFIRM_URL, () =>
        HttpResponse.json(
          {
            title: 'Bad Request',
            status: 400,
            errorCodes: ['auth.emailVerification.invalid'],
          },
          { status: 400 }
        )
      )
    )

    renderPage('anonymous', null, retrySession)

    expect(
      await screen.findByRole('heading', {
        name: 'Не удалось подтвердить адрес',
      })
    ).toBeInTheDocument()
    expect(
      screen.getByText('Ссылка недействительна или устарела.')
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { name: 'Адрес подтверждён' })
    ).not.toBeInTheDocument()

    await waitFor(() => expect(window.location.search).toBe(''))
    expect(retrySession).not.toHaveBeenCalled()
  })

  it('сеть показывает понятную ошибку, а не белый экран', async () => {
    server.use(http.post(CONFIRM_URL, () => HttpResponse.error()))

    renderPage('anonymous')

    expect(
      await screen.findByRole('heading', {
        name: 'Не удалось проверить ссылку',
      })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Нет связи с сервером. Проверьте подключение и повторите попытку.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'На главную' })).toHaveAttribute(
      'href',
      '/'
    )

    await waitFor(() => expect(window.location.search).toBe(''))
  })

  it('без токена сразу говорит, что ссылка неполная, и не делает запрос', () => {
    window.history.replaceState(null, '', '/verify-email')

    const confirmCalls = countRequests(CONFIRM_URL)

    renderPage('anonymous')

    expect(
      screen.getByRole('heading', { name: 'Ссылка неполная' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'В ссылке нет токена подтверждения. Откройте письмо и перейдите по ссылке целиком.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'На главную' })).toBeInTheDocument()
    expect(confirmCalls()).toBe(0)
  })

  it('StrictMode не дублирует запрос подтверждения', async () => {
    const confirmCalls = countRequests(CONFIRM_URL)

    renderPage('anonymous', null, () => {}, true)

    expect(
      await screen.findByRole('heading', { name: 'Адрес подтверждён' })
    ).toBeInTheDocument()
    expect(confirmCalls()).toBe(1)
  })
})
