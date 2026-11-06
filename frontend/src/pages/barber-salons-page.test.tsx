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
  country: 'Russia',
  postalCode: '620000',
  latitude: 56.8389,
  longitude: 60.6057,
  phoneNumber: '+73430000000',
  websiteUrl: 'https://barbershop.example',
  ownerUserId: BARBER.id,
  moderationStatus: 'Active',
  rejectionReason: null,
}

const PENDING_SALON = {
  salonId: '44444444-4444-4444-8444-444444444444',
  name: 'Барбершоп на Проверке',
  city: 'Москва',
  address: 'ул. Проверочная, 5',
  timeZoneId: 'Europe/Moscow',
  isPrimary: false,
  country: 'Russia',
  postalCode: null,
  latitude: 55.75,
  longitude: 37.62,
  phoneNumber: '+79990000000',
  websiteUrl: null,
  ownerUserId: BARBER.id,
  moderationStatus: 'Pending',
  rejectionReason: null,
}

/**
 * Салон, созданный администратором: у него нет владельца, поэтому кнопок правки
 * у барбера быть не должно, хотя салон привязан к его профилю.
 */
const ADMIN_SALON = {
  ...SALON,
  salonId: '66666666-6666-4666-8666-666666666666',
  name: 'Барбершоп администратора',
  isPrimary: false,
  ownerUserId: null,
}

const CATALOG_ITEM = {
  id: '55555555-5555-4555-8555-555555555555',
  name: 'Новый салон',
  city: 'Екатеринбург',
  address: 'ул. Мира, 2',
  timeZoneId: 'Asia/Yekaterinburg',
}

const SALON_BARBER = {
  barberProfileId: PROFILE_ID,
  userId: BARBER.id,
  displayName: 'Тимур Барберов',
  email: 'timur@barbersearch.test',
  isActive: true,
  isVerified: true,
}

function sessionValue(
  status: SessionContextValue['status'],
  user: SessionContextValue['user']
): SessionContextValue {
  return {
    status,
    user,
    signIn: async () => BARBER,
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
    expect(
      within(salonCard!).getByRole('button', { name: 'Барберы салона' })
    ).toBeInTheDocument()

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
    await waitFor(() => {
      expect(listCalls).toBe(2)
    })
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('создание своего салона: POST /api/salons/own и карточка «На проверке»', async () => {
    const user = userEvent.setup()
    let listCalls = 0
    let body: Record<string, unknown> | null = null

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json(listCalls === 1 ? [] : [PENDING_SALON])
      }),
      http.post('/api/salons/own', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>

        return HttpResponse.json({ id: PENDING_SALON.salonId }, { status: 201 })
      })
    )

    renderPage()

    await user.click(
      await screen.findByRole('button', { name: 'Создать салон' })
    )

    const dialog = screen.getByRole('dialog')

    expect(
      within(dialog).getByText(
        'Название нельзя изменить после создания салона.'
      )
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText(/Скопируйте координаты из карт/)
    ).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Название'), PENDING_SALON.name)
    await user.type(
      within(dialog).getByLabelText('Адрес'),
      PENDING_SALON.address
    )
    await user.type(within(dialog).getByLabelText('Город'), PENDING_SALON.city)
    await user.type(within(dialog).getByLabelText('Страна'), 'Россия')
    await user.type(within(dialog).getByLabelText('Широта'), '55.75')
    await user.type(within(dialog).getByLabelText('Долгота'), '37.62')
    await user.type(within(dialog).getByLabelText('Телефон'), '+79990000000')

    await user.click(
      within(dialog).getByRole('button', { name: 'Отправить на проверку' })
    )

    await waitFor(() => {
      expect(body).not.toBeNull()
    })

    expect(body).toMatchObject({
      name: PENDING_SALON.name,
      address: PENDING_SALON.address,
      city: PENDING_SALON.city,
      country: 'Россия',
      latitude: 55.75,
      longitude: 37.62,
      postalCode: null,
      phoneNumber: '+79990000000',
      websiteUrl: null,
    })
    expect(body).toHaveProperty('timeZoneId')

    expect(
      await screen.findByText('Салон отправлен на проверку.')
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(listCalls).toBe(2)
    })

    const card = screen.getByText(PENDING_SALON.name).closest('li')

    expect(card).not.toBeNull()
    expect(within(card!).getByText('На проверке')).toBeInTheDocument()
    expect(
      within(card!).getByText(
        'Салон появится в поиске после проверки администратором.'
      )
    ).toBeInTheDocument()
    expect(within(card!).getByRole('button', { name: 'Изменить адрес' })).toBeInTheDocument()
    expect(
      within(card!).getByRole('button', { name: 'Изменить контакты' })
    ).toBeInTheDocument()
  })

  it('салон, созданный администратором, не показывает правку владельца', async () => {
    mockSalons([ADMIN_SALON])

    renderPage()

    const card = (await screen.findByText(ADMIN_SALON.name)).closest('li')

    expect(card).not.toBeNull()
    expect(
      within(card!).queryByRole('button', { name: 'Барберы салона' })
    ).not.toBeInTheDocument()
    expect(
      within(card!).queryByRole('button', { name: 'Изменить адрес' })
    ).not.toBeInTheDocument()
    expect(
      within(card!).queryByRole('button', { name: 'Изменить контакты' })
    ).not.toBeInTheDocument()
    // Статус и адрес при этом видны: карточка не прячется от барбера.
    expect(within(card!).getByText('Одобрен')).toBeInTheDocument()
  })

  it('владелец открывает состав салона из карточки', async () => {
    const user = userEvent.setup()
    let barbersRequested = 0

    server.use(
      http.get(LIST_URL, () => HttpResponse.json([SALON])),
      http.get(`/api/salons/${SALON_ID}/barbers`, () => {
        barbersRequested += 1

        return HttpResponse.json([SALON_BARBER])
      })
    )

    renderPage()

    const card = (await screen.findByText(SALON.name)).closest('li')

    expect(card).not.toBeNull()

    await user.click(
      within(card!).getByRole('button', { name: 'Барберы салона' })
    )

    const dialog = screen.getByRole('dialog')

    expect(
      within(dialog).getByRole('heading', { name: 'Барберы салона' })
    ).toBeInTheDocument()
    expect(
      await within(dialog).findByText(SALON_BARBER.displayName)
    ).toBeInTheDocument()
    expect(within(dialog).getByText('Владелец')).toBeInTheDocument()
    expect(barbersRequested).toBe(1)
  })

  it('правка владельцем отправляет два независимых PUT и перечитывает список', async () => {
    const user = userEvent.setup()
    let listCalls = 0
    const calls: { method: string; path: string; body: unknown }[] = []

    server.use(
      http.get(LIST_URL, () => {
        listCalls += 1

        return HttpResponse.json([SALON])
      }),
      http.put(`/api/salons/${SALON_ID}/location`, async ({ request }) => {
        calls.push({
          method: request.method,
          path: new URL(request.url).pathname,
          body: await request.json(),
        })

        return new HttpResponse(null, { status: 204 })
      }),
      http.put(`/api/salons/${SALON_ID}/contact-info`, async ({ request }) => {
        calls.push({
          method: request.method,
          path: new URL(request.url).pathname,
          body: await request.json(),
        })

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderPage()

    const card = (await screen.findByText(SALON.name)).closest('li')

    expect(card).not.toBeNull()
    await user.click(
      within(card!).getByRole('button', { name: 'Изменить адрес' })
    )

    const dialog = screen.getByRole('dialog')

    // Правка предзаполняет настоящие значения из DTO, а не просит вводить их заново.
    expect(within(dialog).getByLabelText('Страна')).toHaveValue(SALON.country)
    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue(
      SALON.postalCode
    )
    expect(within(dialog).getByLabelText('Широта')).toHaveValue(
      String(SALON.latitude)
    )
    expect(within(dialog).getByLabelText('Долгота')).toHaveValue(
      String(SALON.longitude)
    )
    expect(within(dialog).getByLabelText('Телефон')).toHaveValue(
      SALON.phoneNumber
    )
    expect(within(dialog).getByLabelText('Сайт')).toHaveValue(SALON.websiteUrl)

    await user.clear(within(dialog).getByLabelText('Телефон'))
    await user.type(within(dialog).getByLabelText('Телефон'), '+79990000001')
    await user.click(within(dialog).getByRole('button', { name: 'Сохранить' }))

    await waitFor(() => {
      expect(calls).toHaveLength(2)
    })

    expect(calls).toStrictEqual([
      {
        method: 'PUT',
        path: `/api/salons/${SALON_ID}/location`,
        body: {
          address: SALON.address,
          city: SALON.city,
          country: SALON.country,
          timeZoneId: SALON.timeZoneId,
          latitude: SALON.latitude,
          longitude: SALON.longitude,
          postalCode: SALON.postalCode,
        },
      },
      {
        method: 'PUT',
        path: `/api/salons/${SALON_ID}/contact-info`,
        body: {
          phoneNumber: '+79990000001',
          websiteUrl: SALON.websiteUrl,
        },
      },
    ])

    expect(
      await screen.findByText('Изменения сохранены')
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(listCalls).toBe(2)
    })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})
