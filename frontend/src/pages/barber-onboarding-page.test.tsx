import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { BarberCabinetPage } from '@/pages/barber-cabinet-page'
import { BarberOnboardingPage } from '@/pages/barber-onboarding-page'
import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const CREATE_URL = '/api/barber-profiles'
const CREATED_ID = '77777777-7777-4777-8777-777777777777'

const BARBER_WITHOUT_PROFILE = {
  id: '33333333-3333-4333-8333-333333333333',
  email: 'barber@barbersearch.test',
  role: 'Barber',
  barberProfileId: null,
  isEmailVerified: true,
} as const

const BARBER_WITH_PROFILE = {
  ...BARBER_WITHOUT_PROFILE,
  barberProfileId: '44444444-4444-4444-8444-444444444444',
} as const

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user'],
  retrySession: () => void = () => {}
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => {},
    signOut: async () => {},
    retrySession,
  }
}

function renderPage(
  status: SessionContextValue['status'] = 'authenticated',
  user: SessionContextValue['user'] = BARBER_WITHOUT_PROFILE,
  retrySession: () => void = () => {}
) {
  return render(
    <SessionContext.Provider value={sessionValue(status, user, retrySession)}>
      <BarberOnboardingPage />
    </SessionContext.Provider>
  )
}

async function fillMinimalForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Как вас представить'), 'Тимур')
  await user.type(screen.getByLabelText('Опыт, лет'), '5')
  await user.click(screen.getByRole('checkbox', { name: 'Русский' }))
}

async function submitForm() {
  await userEvent.click(
    screen.getByRole('button', { name: 'Создать профиль' })
  )
  // Кнопка отключается на время запроса: ждём отправки, а не кадр.
  await waitFor(() => {
    expect(
      screen.queryByRole('button', { name: 'Создать профиль' })
    ).not.toBeInTheDocument()
  })
}

function mockCreate() {
  const createCalls = countRequests(CREATE_URL)

  server.use(
    http.post(CREATE_URL, () =>
      HttpResponse.json({ id: CREATED_ID }, { status: 201 })
    )
  )

  return createCalls
}

beforeEach(() => {
  window.history.replaceState(null, '', '/')
})

describe('BarberOnboardingPage', () => {
  it('аноним видит гейт входа и форма не шлёт запрос', async () => {
    const createCalls = countRequests(CREATE_URL)

    renderPage('anonymous', null)

    expect(
      screen.getByText('Войдите, чтобы создать профиль барбера')
    ).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()
    expect(createCalls()).toBe(0)
  })

  it('клиент видит гейт роли и форма не шлёт запрос', async () => {
    const createCalls = countRequests(CREATE_URL)

    renderPage('authenticated', { ...BARBER_WITHOUT_PROFILE, role: 'Customer' })

    expect(screen.getByText('Это кабинет барбера')).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()
    expect(createCalls()).toBe(0)
  })

  it('барбер с профилем уходит в кабинет и не видит форму дважды', async () => {
    renderPage('authenticated', BARBER_WITH_PROFILE)

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()
  })

  it('201: обновляет сессию, повторяет проверку и ведёт в кабинет', async () => {
    const user = userEvent.setup()
    const createCalls = mockCreate()
    const refreshCalls = countRequests('/api/auth/refresh')
    const retrySession = vi.fn()

    renderPage('authenticated', BARBER_WITHOUT_PROFILE, retrySession)

    await fillMinimalForm(user)
    await submitForm()

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })

    expect(createCalls()).toBe(1)
    expect(refreshCalls()).toBe(1)
    expect(retrySession).toHaveBeenCalledTimes(1)
  })

  it('409 «профиль уже есть»: тот же переход без текста ошибки', async () => {
    const user = userEvent.setup()
    const createCalls = countRequests(CREATE_URL)
    const refreshCalls = countRequests('/api/auth/refresh')
    const retrySession = vi.fn()

    server.use(
      http.post(CREATE_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.create.alreadyExists'],
          }),
          { status: 409 }
        )
      )
    )

    renderPage('authenticated', BARBER_WITHOUT_PROFILE, retrySession)

    await fillMinimalForm(user)
    await submitForm()

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })

    expect(createCalls()).toBe(1)
    expect(refreshCalls()).toBe(1)
    expect(retrySession).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('профиль создан, но сессия мертва: карточка входа, без второго POST', async () => {
    const user = userEvent.setup()
    const createCalls = mockCreate()

    server.use(
      http.post('/api/auth/refresh', () =>
        HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.refresh.invalid'] }),
          { status: 401 }
        )
      )
    )

    renderPage()

    await fillMinimalForm(user)
    await submitForm()

    expect(
      await screen.findByText('Профиль создан, войдите снова')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Войти' })
    ).toBeInTheDocument()
    expect(window.location.pathname).toBe('/')
    expect(createCalls()).toBe(1)
  })

  it('продление не удалось: карточка с повтором, профиль не отправляется снова', async () => {
    const user = userEvent.setup()
    const createCalls = mockCreate()
    let refreshAttempts = 0

    server.use(
      http.post('/api/auth/refresh', () => {
        refreshAttempts += 1

        return refreshAttempts === 1
          ? HttpResponse.json(problemDetails(500), { status: 500 })
          : new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()

    await fillMinimalForm(user)
    await submitForm()

    expect(
      await screen.findByText('Профиль создан, но сессия не обновилась')
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Повторить' }))

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })
    expect(refreshAttempts).toBe(2)
    expect(createCalls()).toBe(1)
  })
})

describe('BarberProfileMissingGate', () => {
  it('барбер без профиля видит CTA и попадает на онбординг', async () => {
    render(
      <SessionContext.Provider
        value={sessionValue('authenticated', BARBER_WITHOUT_PROFILE)}
      >
        <BarberCabinetPage />
      </SessionContext.Provider>
    )

    expect(
      await screen.findByText('Профиль ещё не создан')
    ).toBeInTheDocument()

    await userEvent.click(
      screen.getByRole('button', { name: 'Создать профиль барбера' })
    )

    expect(window.location.pathname).toBe('/barber/onboarding')
  })
})
