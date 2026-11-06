import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SessionContext, type SessionContextValue } from '@/hooks/use-session'
import { BarberSalonsPage } from '@/pages/barber-salons-page'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const PROFILE_ID = '11111111-1111-4111-8111-111111111111'
const SALON_ID = '22222222-2222-4222-8222-222222222222'
const LIST_URL = `/api/barber-profiles/${PROFILE_ID}/salons`

const BARBER = {
  id: '33333333-3333-4333-8333-333333333333',
  email: 'barber@barbersearch.test',
  role: 'Barber',
  barberProfileId: PROFILE_ID,
  isEmailVerified: true,
} as const

const SALON = {
  salonId: SALON_ID,
  name: 'Барбершоп на Ленина',
  city: 'Екатеринбург',
  address: 'ул. Ленина, 1',
  timeZoneId: 'Asia/Yekaterinburg',
  isPrimary: true,
}

const CATALOG_ITEM = {
  id: '55555555-5555-4555-8555-555555555555',
  name: 'Новый салон',
  city: 'Екатеринбург',
  address: 'ул. Мира, 2',
  timeZoneId: 'Asia/Yekaterinburg',
}

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user']
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => {},
    signOut: async () => {},
    retrySession: () => {},
  }
}

function renderPage(
  status: SessionContextValue['status'] = 'authenticated',
  user: SessionContextValue['user'] = BARBER
) {
  return render(
    <SessionContext.Provider value={sessionValue(status, user)}>
      <BarberSalonsPage />
    </SessionContext.Provider>
  )
}

function mockSalons(rows: unknown[], onRead?: () => void) {
  server.use(
    http.get(LIST_URL, () => {
      onRead?.()

      return HttpResponse.json(rows)
    })
  )
}

describe('BarberSalonsPage', () => {
  it('аноним видит гейт входа и не запрашивает салоны', () => {
    const requests = countRequests(LIST_URL)

    renderPage('anonymous', null)

    expect(
      screen.getByText('Войдите, чтобы выбрать салон')
    ).toBeInTheDocument()
    expect(requests()).toBe(0)
  })

  it('клиент и администратор видят гейт роли без запросов', () => {
    const requests = countRequests(LIST_URL)

    const { unmount } = renderPage('authenticated', {
      ...BARBER,
      role: 'Customer',
      barberProfileId: null,
    })

    expect(screen.getByText('Это кабинет барбера')).toBeInTheDocument()
    unmount()

    renderPage('authenticated', {
      ...BARBER,
      role: 'Admin',
      barberProfileId: null,
    })

    expect(screen.getByText('Вы администратор')).toBeInTheDocument()
    expect(requests()).toBe(0)
  })

  it('барбер без профиля видит CTA в онбординг без запросов', () => {
    const requests = countRequests(LIST_URL)

    renderPage('authenticated', { ...BARBER, barberProfileId: null })

    expect(screen.getByText('Профиль ещё не создан')).toBeInTheDocument()
    expect(requests()).toBe(0)
  })

  it('показывает основной салон, адрес, часовой пояс и третий пункт навигации', async () => {
    mockSalons([SALON])

    renderPage()

    expect(
      await screen.findByRole('heading', { name: 'Мои салоны', level: 1 })
    ).toBeInTheDocument()

    const salonCard = screen.getByText('Барбершоп на Ленина').closest('li')

    expect(salonCard).not.toBeNull()
    expect(within(salonCard!).getByText('Основной')).toBeInTheDocument()
    expect(within(salonCard!).getByText(/Ленина, 1/)).toBeInTheDocument()
    expect(within(salonCard!).getByText(/Asia\/Yekaterinburg/)).toBeInTheDocument()

    const nav = screen.getByRole('navigation', {
      name: 'Разделы кабинета барбера',
    })

    expect(within(nav).getByRole('link', { name: 'Салоны' })).toHaveAttribute(
      'href',
      '/barber/salons'
    )
    expect(within(nav).getByRole('link', { name: 'Салоны' })).toHaveAttribute(
      'aria-current',
      'page'
    )
  })

  it('пустой список предлагает добавить салон и объясняет последствия', async () => {
    mockSalons([])

    renderPage()

    expect(await screen.findByText('Салон не добавлен')).toBeInTheDocument()
    expect(
      screen.getByText('Без салона расписание и запись не работают.')
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Добавить салон' })
    ).toBeInTheDocument()
  })

  it('привязка: POST, перечитывание списка и объявление результата', async () => {
    const user = userEvent.setup()
    let listCalls = 0
    let body: unknown = null

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json(listCalls === 1 ? [] : [SALON])
      }),
      http.get('/api/salons', () => HttpResponse.json([CATALOG_ITEM])),
      http.post(LIST_URL, async ({ request }) => {
        body = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Добавить салон' }))

    const dialog = screen.getByRole('dialog')

    await user.type(within(dialog).getByLabelText('Поиск'), 'Новый')
    await user.click(await within(dialog).findByRole('radio'))
    await user.click(within(dialog).getByRole('button', { name: 'Добавить' }))

    await waitFor(() => {
      expect(body).toEqual({ salonId: CATALOG_ITEM.id, isPrimary: true })
    })

    expect(await screen.findByText('Салон добавлен.')).toBeInTheDocument()
    expect(listCalls).toBe(2)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByText('Барбершоп на Ленина')).toBeInTheDocument()
  })

  it('409 дубликата перечитывает «Мои салоны», диалог остаётся', async () => {
    const user = userEvent.setup()
    let listCalls = 0

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json([SALON])
      }),
      http.get('/api/salons', () => HttpResponse.json([CATALOG_ITEM])),
      http.post(LIST_URL, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.addSalon.duplicate'],
          }),
          { status: 409 }
        )
      )
    )

    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Добавить салон' }))

    const dialog = screen.getByRole('dialog')

    await user.type(within(dialog).getByLabelText('Поиск'), 'Новый')
    await user.click(await within(dialog).findByRole('radio'))
    await user.click(within(dialog).getByRole('button', { name: 'Добавить' }))

    expect(
      await screen.findByText('Этот салон уже добавлен.')
    ).toBeInTheDocument()
    expect(listCalls).toBe(2)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
})
