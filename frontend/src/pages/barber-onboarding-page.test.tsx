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
const MINE_URL = '/api/barber-requests/mine'
const CREATED_ID = '77777777-7777-4777-8777-777777777777'

/** Одобренная заявка — источник имени, опыта и «О себе» для формы профиля. */
const APPROVED_REQUEST = {
  id: '55555555-5555-4555-8555-555555555555',
  status: 'Approved',
  displayName: 'E2E Заявитель',
  yearsOfExperience: 6,
  city: 'Москва',
  about: 'Стрижки и борода.',
  hasSalon: false,
  createdAt: '2026-11-01T10:00:00+00:00',
  reviewedAt: '2026-11-02T10:00:00+00:00',
  rejectionReason: null,
} as const

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
    signIn: async () => BARBER_WITHOUT_PROFILE,
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
  // Форма появляется только после чтения заявки (`GET /barber-requests/mine`).
  await screen.findByLabelText('Как вас представить')
  await user.type(screen.getByLabelText('Как вас представить'), 'Тимур')
  await user.type(screen.getByLabelText('Опыт, лет'), '5')
  await user.selectOptions(screen.getByLabelText('Грейд'), 'MiddleBarber')
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
    const mineCalls = countRequests(MINE_URL)

    renderPage('anonymous', null)

    expect(
      screen.getByText('Войдите, чтобы создать профиль барбера')
    ).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()
    expect(createCalls()).toBe(0)
    expect(mineCalls()).toBe(0)
  })

  it('клиент видит гейт роли и форма не шлёт запрос', async () => {
    const createCalls = countRequests(CREATE_URL)
    const mineCalls = countRequests(MINE_URL)

    renderPage('authenticated', { ...BARBER_WITHOUT_PROFILE, role: 'Customer' })

    expect(screen.getByText('Это кабинет барбера')).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()
    expect(createCalls()).toBe(0)
    expect(mineCalls()).toBe(0)
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

  it('201: обновляет сессию и показывает шаг «Добавить салон»', async () => {
    const user = userEvent.setup()
    const createCalls = mockCreate()
    const refreshCalls = countRequests('/api/auth/refresh')
    const retrySession = vi.fn()

    renderPage('authenticated', BARBER_WITHOUT_PROFILE, retrySession)

    await fillMinimalForm(user)
    await submitForm()

    expect(await screen.findByText('Добавьте место приёма')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Выбрать салон' })
    ).toBeInTheDocument()

    // Шаг не блокирует кабинет: «Пропустить» ведёт в /barber.
    await user.click(screen.getByRole('button', { name: 'Пропустить' }))

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })

    expect(createCalls()).toBe(1)
    expect(refreshCalls()).toBe(1)
    expect(retrySession).toHaveBeenCalledTimes(1)
  })

  it('одобренная заявка заполняет имя, опыт и «О себе» только для чтения', async () => {
    let body: unknown = null

    server.use(
      http.get(MINE_URL, () => HttpResponse.json({ request: APPROVED_REQUEST })),
      http.post(CREATE_URL, async ({ request }) => {
        body = await request.json()

        return HttpResponse.json({ id: CREATED_ID }, { status: 201 })
      })
    )

    renderPage()

    const displayName = await screen.findByLabelText('Как вас представить')

    expect(displayName).toHaveValue('E2E Заявитель')
    expect(displayName).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Опыт, лет')).toHaveValue(6)
    expect(screen.getByLabelText('Опыт, лет')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('О себе')).toHaveValue('Стрижки и борода.')
    expect(screen.getByLabelText('О себе')).toHaveAttribute('readonly')

    await userEvent.selectOptions(
      screen.getByLabelText('Грейд'),
      'SeniorBarber'
    )
    await userEvent.click(screen.getByRole('checkbox', { name: 'Русский' }))
    await userEvent.click(
      screen.getByRole('button', { name: 'Создать профиль' })
    )

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(body).toMatchObject({
      displayName: 'E2E Заявитель',
      yearsOfExperience: 6,
      grade: 'SeniorBarber',
      bio: 'Стрижки и борода.',
    })
    expect(await screen.findByText('Добавьте место приёма')).toBeInTheDocument()
  })

  it('заявка без «О себе»: описание в профиле остаётся редактируемым', async () => {
    server.use(
      http.get(MINE_URL, () =>
        HttpResponse.json({ request: { ...APPROVED_REQUEST, about: null } })
      )
    )

    renderPage()

    const bio = await screen.findByLabelText('О себе')

    expect(bio).toHaveValue('')
    expect(bio).not.toHaveAttribute('readonly')
    expect(screen.getByLabelText('Как вас представить')).toHaveAttribute(
      'readonly'
    )
  })

  it('заявка на рассмотрении не заполняет профиль', async () => {
    server.use(
      http.get(MINE_URL, () =>
        HttpResponse.json({
          request: { ...APPROVED_REQUEST, status: 'Pending' },
        })
      )
    )

    renderPage()

    const displayName = await screen.findByLabelText('Как вас представить')

    expect(displayName).toHaveValue('')
    expect(displayName).not.toHaveAttribute('readonly')
  })

  it('ошибка чтения заявки: форма закрыта, «Повторить» перечитывает', async () => {
    const mineCalls = countRequests(MINE_URL)
    let attempts = 0

    server.use(
      http.get(MINE_URL, () => {
        attempts += 1

        return attempts === 1
          ? HttpResponse.json(problemDetails(500), { status: 500 })
          : HttpResponse.json({ request: null })
      })
    )

    renderPage()

    expect(
      await screen.findByText('Не удалось проверить заявку.')
    ).toBeInTheDocument()
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(
      await screen.findByLabelText('Как вас представить')
    ).toBeInTheDocument()
    expect(mineCalls()).toBe(2)
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

    // Профиль уже создан: после успешного продления остаётся шаг салона.
    expect(await screen.findByText('Добавьте место приёма')).toBeInTheDocument()
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
