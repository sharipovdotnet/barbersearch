import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'

import { ProfileProvider } from '@/components/profile-provider'
import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import type { MyProfile } from '@/lib/my-profile'
import { AccountSecurityPage } from '@/pages/account-security-page'
import {
  noContent,
  problemDetails,
} from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const RESEND_URL = '/api/auth/email-verification'

function profile(
  id: string,
  isEmailVerified: boolean,
  email = 'client@barbersearch.test'
): MyProfile {
  return {
    id,
    email,
    firstName: 'Мария',
    lastName: 'Кузнецова',
    phoneNumber: null,
    avatarUrl: null,
    isAvatarBlocked: false,
    role: 'Customer',
    barberProfileId: null,
    isEmailVerified,
    isPhoneVerified: false,
    createdAt: '2026-09-01T10:00:00Z',
    lastLoginAt: null,
  }
}

function sessionUser(value: MyProfile): SessionUser {
  return {
    id: value.id,
    email: value.email,
    role: value.role,
    barberProfileId: value.barberProfileId,
    isEmailVerified: value.isEmailVerified,
  }
}

function sessionValue(user: MyProfile): SessionContextValue {
  return {
    status: 'authenticated',
    user: sessionUser(user),
    signIn: async () => sessionUser(user),
    signOut: async () => {},
    retrySession: () => {},
  }
}

function renderPage(user: MyProfile) {
  return render(
    <SessionContext.Provider value={sessionValue(user)}>
      <ProfileProvider>
        <AccountSecurityPage />
      </ProfileProvider>
    </SessionContext.Provider>
  )
}

beforeEach(() => {
  window.history.replaceState(null, '', '/account/security')
})

describe('AccountSecurityPage: повторная отправка письма', () => {
  it('после 204 объявляет об отправке и включает кулдаун', async () => {
    const user = userEvent.setup()
    const current = profile('11111111-1111-4111-8111-111111111111', false)
    const resendCalls = countRequests(RESEND_URL)

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(current)),
      http.post(RESEND_URL, () => noContent())
    )

    renderPage(current)

    await user.click(
      await screen.findByRole('button', { name: 'Отправить письмо ещё раз' })
    )

    expect(
      await screen.findByText(
        'Письмо отправлено на client@barbersearch.test.'
      )
    ).toBeInTheDocument()
    expect(resendCalls()).toBe(1)

    const cooldown = await screen.findByRole('button', {
      name: /^Повторить через \d+ с$/,
    })
    expect(cooldown).toBeDisabled()
  })

  it('409 кулдаун показывает тот же текст и блокирует кнопку', async () => {
    const user = userEvent.setup()
    const current = profile('22222222-2222-4222-8222-222222222222', false)

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(current)),
      http.post(RESEND_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['auth.emailVerification.cooldown'],
          }),
          { status: 409 }
        )
      )
    )

    renderPage(current)

    await user.click(
      await screen.findByRole('button', { name: 'Отправить письмо ещё раз' })
    )

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(
      'Письмо уже отправлено. Повторите через минуту.'
    )
    expect(
      screen.getByRole('button', { name: /^Повторить через \d+ с$/ })
    ).toBeDisabled()
  })

  it('429 от лимитера показывает текст кулдауна', async () => {
    const user = userEvent.setup()
    const current = profile('33333333-3333-4333-8333-333333333333', false)

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(current)),
      http.post(RESEND_URL, () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    renderPage(current)

    await user.click(
      await screen.findByRole('button', { name: 'Отправить письмо ещё раз' })
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Письмо уже отправлено. Повторите через минуту.'
    )
  })

  it('409 «почта не настроена» говорит правду и оставляет кнопку доступной', async () => {
    const user = userEvent.setup()
    const current = profile('44444444-4444-4444-8444-444444444444', false)

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(current)),
      http.post(RESEND_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['auth.emailVerification.unavailable'],
          }),
          { status: 409 }
        )
      )
    )

    renderPage(current)

    await user.click(
      await screen.findByRole('button', { name: 'Отправить письмо ещё раз' })
    )

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Отправка писем пока не настроена.'
    )
    expect(
      screen.getByRole('button', { name: 'Отправить письмо ещё раз' })
    ).toBeEnabled()
  })
})

describe('AccountSecurityPage: статус адреса', () => {
  it('после 204 перечитывает профиль и показывает подтверждённый адрес', async () => {
    const user = userEvent.setup()
    const current = profile('55555555-5555-4555-8555-555555555555', false)
    const verified = { ...current, isEmailVerified: true }
    let profileReads = 0

    server.use(
      http.get('/api/users/me', () => {
        profileReads += 1

        return HttpResponse.json(profileReads === 1 ? current : verified)
      }),
      http.post(RESEND_URL, () => noContent())
    )

    renderPage(current)

    await user.click(
      await screen.findByRole('button', { name: 'Отправить письмо ещё раз' })
    )

    expect(
      await screen.findByText('Адрес подтверждён.')
    ).toBeInTheDocument()
    await waitFor(() => expect(profileReads).toBe(2))
    expect(
      screen.queryByRole('button', { name: /Повторить через|Отправить письмо/ })
    ).not.toBeInTheDocument()
  })

  it('подтверждённый адрес не предлагает повторную отправку', async () => {
    const current = profile('66666666-6666-4666-8666-666666666666', true)

    server.use(http.get('/api/users/me', () => HttpResponse.json(current)))

    renderPage(current)

    expect(await screen.findByText('Адрес подтверждён.')).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отправить письмо ещё раз' })
    ).not.toBeInTheDocument()
  })
})

describe('AccountSecurityPage: восстановление пароля', () => {
  it('ссылка «Забыли пароль?» ведёт на /forgot-password', async () => {
    const user = userEvent.setup()
    const current = profile('88888888-8888-4888-8888-888888888888', true)

    server.use(http.get('/api/users/me', () => HttpResponse.json(current)))

    renderPage(current)

    const link = await screen.findByRole('link', { name: 'Забыли пароль?' })
    expect(link).toHaveAttribute('href', '/forgot-password')

    await user.click(link)

    expect(window.location.pathname).toBe('/forgot-password')
  })
})

describe('AccountSecurityPage: приземление после регистрации', () => {
  it('показывает «письмо отправлено» и сразу убирает query из адреса', async () => {
    const current = profile('77777777-7777-4777-8777-777777777777', false)

    window.history.replaceState(
      null,
      '',
      '/account/security?verification=sent'
    )

    server.use(http.get('/api/users/me', () => HttpResponse.json(current)))

    renderPage(current)

    expect(
      await screen.findByText(
        'Письмо отправлено на client@barbersearch.test. Откройте письмо и перейдите по ссылке, чтобы подтвердить адрес.'
      )
    ).toBeInTheDocument()

    await waitFor(() => expect(window.location.search).toBe(''))
  })
})
