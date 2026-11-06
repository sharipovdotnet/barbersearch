import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import type { BarberRequest } from '@/lib/barber-requests'
import { BecomeABarberPage } from '@/pages/become-a-barber-page'
import { problemDetails, unauthorized } from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Страница `/become-a-barber`: кто что видит и кто какие запросы делает.
 *
 * Главное обещание среза — анонимный посетитель получает маркетинговый текст и
 * **ни одного** запроса, а форма и статус монтируются только для клиента. Роли
 * барбера и администратора уводят в их рабочие зоны.
 */

const MINE_URL = '/api/barber-requests/mine'

const PENDING: BarberRequest = {
  id: '9ea00000-0000-4000-8000-000000000001',
  status: 'Pending',
  displayName: 'Иван Петров',
  yearsOfExperience: 7,
  city: 'Москва',
  about: 'Стригу и брею',
  hasSalon: false,
  createdAt: '2026-11-06T10:00:00+00:00',
  reviewedAt: null,
  rejectionReason: null,
}

const REJECTED: BarberRequest = {
  ...PENDING,
  status: 'Rejected',
  reviewedAt: '2026-11-06T12:00:00+00:00',
  rejectionReason: 'Не хватает подтверждённого опыта',
}

const CUSTOMER = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'client@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
} as const

const BARBER = {
  ...CUSTOMER,
  role: 'Barber',
  barberProfileId: '22222222-2222-4222-8222-222222222222',
}

const NEW_BARBER = { ...BARBER, barberProfileId: null }

const ADMIN = { ...CUSTOMER, role: 'Admin' }

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user']
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => CUSTOMER,
    signOut: async () => {},
    retrySession: () => {},
  }
}

function renderPage(
  status: SessionContextValue['status'] = 'authenticated',
  user: SessionContextValue['user'] = CUSTOMER
) {
  window.history.replaceState(null, '', '/become-a-barber')

  return render(
    <SessionContext.Provider value={sessionValue(status, user)}>
      <BecomeABarberPage />
    </SessionContext.Provider>
  )
}

function mockMine(body: { request: BarberRequest | null }) {
  const requests: string[] = []

  server.use(
    http.get(MINE_URL, ({ request }) => {
      requests.push(request.url)

      return HttpResponse.json(body)
    })
  )

  return requests
}

describe('BecomeABarberPage', () => {
  it('аноним видит текст, «Войти» и «Зарегистрироваться» — и не делает запросов', () => {
    const paths = recordRequests()

    renderPage('anonymous', null)

    expect(
      screen.getByRole('heading', { name: 'Стать барбером', level: 1 })
    ).toBeInTheDocument()
    expect(screen.getByText('Что даёт статус барбера')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Войти' })).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: /Зарегистрироваться/ })
    ).toHaveAttribute('href', '/register')

    // Формы и статуса нет: заявку подаёт только вошедший клиент.
    expect(
      screen.queryByRole('button', { name: 'Отправить заявку' })
    ).not.toBeInTheDocument()
    expect(paths).toStrictEqual([])
  })

  it('loading показывает скелетон, unavailable — карточку недоступной сессии', () => {
    const { unmount } = renderPage('loading', null)

    expect(screen.getByText('Проверяем сессию…')).toBeInTheDocument()

    unmount()

    renderPage('unavailable', null)

    expect(screen.getByText('Не удалось проверить сессию')).toBeInTheDocument()
  })

  it('клиент с заявкой видит статус и не видит форму', async () => {
    const requests = mockMine({ request: PENDING })

    renderPage()

    expect(
      await screen.findByRole('heading', { name: 'Заявка на рассмотрении' })
    ).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('На рассмотрении')
    expect(
      screen.queryByLabelText('Как вас представить')
    ).not.toBeInTheDocument()
    expect(requests).toHaveLength(1)
  })

  it('клиент без заявки получает форму', async () => {
    mockMine({ request: null })

    renderPage()

    expect(
      await screen.findByRole('heading', { name: 'Заявка на роль барбера' })
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Как вас представить')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Отправить заявку' })
    ).toBeInTheDocument()
  })

  it('после 201 вся секция меняется на одну карточку «Заявка отправлена»', async () => {
    const user = userEvent.setup()

    mockMine({ request: null })
    server.use(
      http.post('/api/barber-requests', () =>
        HttpResponse.json(PENDING, { status: 201 })
      )
    )

    renderPage()

    await screen.findByRole('heading', { name: 'Заявка на роль барбера' })

    await user.type(
      screen.getByLabelText('Как вас представить'),
      PENDING.displayName
    )
    await user.type(
      screen.getByLabelText('Опыт, лет'),
      String(PENDING.yearsOfExperience)
    )
    await user.type(screen.getByLabelText('Город'), PENDING.city)
    await user.click(screen.getByRole('button', { name: 'Отправить заявку' }))

    expect(
      await screen.findByRole('heading', { name: 'Заявка отправлена' })
    ).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('На рассмотрении')
    // Старый заголовок и интро формы не остаются над новой карточкой.
    expect(
      screen.queryByRole('heading', { name: 'Заявка на роль барбера' })
    ).not.toBeInTheDocument()
  })

  it('отклонённая заявка показывает причину и даёт подать новую', async () => {
    const user = userEvent.setup()

    mockMine({ request: REJECTED })
    renderPage()

    expect(
      await screen.findByRole('heading', { name: 'Заявка отклонена' })
    ).toBeInTheDocument()
    expect(
      screen.getByText('Не хватает подтверждённого опыта')
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Подать заявку снова' }))

    expect(screen.getByLabelText('Как вас представить')).toBeInTheDocument()
  })

  it('барбер уходит в кабинет, а без профиля — в онбординг, без чтения заявки', async () => {
    const user = userEvent.setup()
    const paths = recordRequests()

    const { unmount } = renderPage('authenticated', BARBER)

    expect(
      screen.getByRole('heading', { name: 'Вы уже барбер' })
    ).toBeInTheDocument()

    await user.click(
      screen.getByRole('button', { name: 'Открыть кабинет барбера' })
    )

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber')
    })

    unmount()
    window.history.replaceState(null, '', '/become-a-barber')

    renderPage('authenticated', NEW_BARBER)

    await user.click(
      screen.getByRole('button', { name: 'Создать профиль барбера' })
    )

    await waitFor(() => {
      expect(window.location.pathname).toBe('/barber/onboarding')
    })

    // Заявка барберу не нужна: `GET /mine` делает только ветка клиента.
    expect(paths).not.toContain(MINE_URL)
  })

  it('администратор получает ссылку в раздел заявок, без запроса заявки', async () => {
    const user = userEvent.setup()
    const paths = recordRequests()

    renderPage('authenticated', ADMIN)

    expect(
      screen.getByRole('heading', { name: 'Вы администратор' })
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Открыть заявки' }))

    await waitFor(() => {
      expect(window.location.pathname).toBe('/admin/barber-requests')
    })
    expect(paths).toStrictEqual([])
  })

  it('401 на чтении заявки показывает гейт входа, а не вечный скелетон', async () => {
    server.use(http.get(MINE_URL, () => unauthorized()))

    renderPage()

    expect(await screen.findByText('Сессия истекла')).toBeInTheDocument()
    expect(
      screen.queryByText('Загружаем заявку…')
    ).not.toBeInTheDocument()
  })

  it('ошибка чтения заявки: русский текст и «Повторить»', async () => {
    server.use(
      http.get(MINE_URL, () =>
        HttpResponse.json(problemDetails(429), { status: 429 })
      )
    )

    renderPage()

    expect(
      await screen.findByText('Не удалось проверить заявку')
    ).toBeInTheDocument()
    expect(
      screen.getByText('Слишком много запросов. Подождите минуту и повторите.')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Повторить' })
    ).toBeInTheDocument()
  })
})
