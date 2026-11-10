import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import {
  SessionContext,
  type SessionContextValue,
} from '@/hooks/use-session'
import type { BarberProfile } from '@/lib/catalog'
import { BarberProfilePage } from '@/pages/barber-profile-page'
import {
  catalogueHairService,
  catalogueSpecialization,
  noContent,
  problemDetails,
  testBarberProfile,
} from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const PROFILE_URL = `/api/barber-profiles/${testBarberProfile.id}`

const BARBER = {
  id: '33333333-3333-4333-8333-333333333333',
  email: 'barber@barbersearch.test',
  role: 'Barber',
  barberProfileId: testBarberProfile.id,
  isEmailVerified: true,
} as const

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
      <BarberProfilePage />
    </SessionContext.Provider>
  )
}

function cloneProfile(profile: BarberProfile): BarberProfile {
  return JSON.parse(JSON.stringify(profile)) as BarberProfile
}

/** Свой профиль: GET отвечает текущим состоянием, мутации в тестах его меняют. */
function mockOwnProfile(initial: BarberProfile = testBarberProfile) {
  let current = cloneProfile(initial)
  let getCount = 0

  server.use(
    http.get(PROFILE_URL, () => {
      getCount += 1

      return HttpResponse.json(current)
    })
  )

  return {
    getCount: () => getCount,
    current: () => current,
    setProfile: (next: BarberProfile) => {
      current = next
    },
  }
}

async function waitForProfile() {
  await screen.findByRole('heading', { level: 1, name: 'Профиль' })
  await screen.findByLabelText('Как вас представить')
}

function serviceRow(name: string): HTMLElement {
  const row = screen
    .getAllByText(name)
    .map((node) => node.closest('li'))
    .find((item): item is HTMLLIElement => item instanceof HTMLElement)

  if (!row) {
    throw new Error(`Строка услуги «${name}» не найдена`)
  }

  return row
}

describe('BarberProfilePage: гейты', () => {
  it('загрузка сессии: скелетон и ноль запросов', () => {
    const requests = recordRequests()

    renderPage('loading', null)

    expect(screen.getByRole('status')).toHaveTextContent('Загружаем профиль…')
    expect(requests.filter((path) => path.startsWith('/api/barber-profiles')))
      .toEqual([])
  })

  it('аноним видит гейт входа и не делает запросов', () => {
    const requests = recordRequests()

    renderPage('anonymous', null)

    expect(
      screen.getByText('Войдите, чтобы открыть профиль')
    ).toBeInTheDocument()
    expect(requests).toEqual([])
  })

  it('клиент видит объяснение роли и не делает запросов', () => {
    const requests = recordRequests()

    renderPage('authenticated', {
      ...BARBER,
      role: 'Customer',
      barberProfileId: null,
    })

    expect(screen.getByText('Это кабинет барбера')).toBeInTheDocument()
    expect(requests).toEqual([])
  })

  it('барбер без профиля видит свой гейт и не делает запросов', () => {
    const requests = recordRequests()

    renderPage('authenticated', {
      ...BARBER,
      barberProfileId: null,
    })

    expect(screen.getByText('Профиль ещё не создан')).toBeInTheDocument()
    expect(requests).toEqual([])
  })
})

describe('BarberProfilePage: чтение', () => {
  it('делает ровно один GET и показывает карточку и услуги', async () => {
    const profile = mockOwnProfile()
    const requests = recordRequests()

    renderPage()
    await waitForProfile()

    expect(profile.getCount()).toBe(1)
    expect(screen.getByLabelText('О себе')).toHaveValue('Стригу с 2015 года.')
    expect(screen.getAllByText('Стрижка').length).toBeGreaterThan(0)
    expect(screen.getByText('Оформление бороды')).toBeInTheDocument()
    expect(screen.getByText('Скрыта')).toBeInTheDocument()
    expect(screen.getByText('Барберинг')).toBeInTheDocument()

    // Справочники не тянутся вместе со страницей.
    expect(requests).not.toContain('/api/hair-services')
    expect(requests).not.toContain('/api/specializations')
  })
})

describe('BarberProfilePage: мутации', () => {
  it('сохраняет карточку: PUT, объявление и повторное чтение', async () => {
    const profile = mockOwnProfile()
    let body: unknown = null

    server.use(
      http.put(PROFILE_URL, async ({ request }) => {
        body = await request.json()
        const draft = body as { bio: string | null }

        profile.setProfile({ ...profile.current(), bio: draft.bio })

        return noContent()
      })
    )

    renderPage()
    await waitForProfile()

    fireEvent.change(screen.getByLabelText('О себе'), {
      target: { value: 'Стрижки, борода и укладка.' },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Сохранить' }))

    expect(await screen.findByText('Профиль сохранён.')).toBeInTheDocument()
    expect(body).not.toHaveProperty('grade')
    expect(body).toMatchObject({ bio: 'Стрижки, борода и укладка.' })

    await waitFor(() => {
      expect(profile.getCount()).toBe(2)
    })
  })

  it('добавляет услугу: POST с hairServiceId, ценой и длительностью', async () => {
    const profile = mockOwnProfile()
    let body: unknown = null

    server.use(
      http.post(`${PROFILE_URL}/services`, async ({ request }) => {
        body = await request.json()

        return noContent()
      })
    )

    renderPage()
    await waitForProfile()

    await userEvent.click(
      screen.getByRole('button', { name: 'Добавить услугу' })
    )

    expect(
      await screen.findByRole('option', { name: 'Стрижка' })
    ).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Услуга'), {
      target: { value: catalogueHairService.id },
    })

    expect(screen.getByLabelText('Длительность, мин')).toHaveValue(45)

    fireEvent.change(screen.getByLabelText('Цена, ₽'), {
      target: { value: '2000' },
    })

    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Добавить',
      })
    )

    expect(await screen.findByText('Услуга добавлена.')).toBeInTheDocument()
    expect(body).toStrictEqual({
      hairServiceId: catalogueHairService.id,
      price: 2000,
      durationMinutes: 45,
    })

    await waitFor(() => {
      expect(profile.getCount()).toBe(2)
    })
  })

  it('меняет цену: PUT .../services/{hairServiceId}', async () => {
    const profile = mockOwnProfile()
    let url = ''
    let body: unknown = null

    server.use(
      http.put(`${PROFILE_URL}/services/:hairServiceId`, async ({ request }) => {
        url = request.url
        body = await request.json()

        return noContent()
      })
    )

    renderPage()
    await waitForProfile()

    await userEvent.click(
      within(serviceRow('Стрижка')).getByRole('button', {
        name: 'Изменить цену',
      })
    )

    const dialog = screen.getByRole('dialog')

    expect(within(dialog).getByLabelText('Цена, ₽')).toHaveValue(1500)

    fireEvent.change(within(dialog).getByLabelText('Цена, ₽'), {
      target: { value: '2000' },
    })
    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Сохранить' })
    )

    expect(await screen.findByText('Цена обновлена.')).toBeInTheDocument()
    expect(new URL(url).pathname).toBe(
      `${PROFILE_URL}/services/${testBarberProfile.services[0].hairServiceId}`
    )
    expect(body).toStrictEqual({ price: 2000, durationMinutes: 45 })

    await waitFor(() => {
      expect(profile.getCount()).toBe(2)
    })
  })

  it('скрывает услугу: DELETE, строка остаётся со статусом «Скрыта»', async () => {
    const profile = mockOwnProfile()

    server.use(
      http.delete(`${PROFILE_URL}/services/:hairServiceId`, ({ params }) => {
        const hairServiceId = params.hairServiceId as string

        profile.setProfile({
          ...profile.current(),
          services: profile.current().services.map((service) =>
            service.hairServiceId === hairServiceId
              ? { ...service, isActive: false }
              : service
          ),
        })

        return noContent()
      })
    )

    renderPage()
    await waitForProfile()

    await userEvent.click(
      within(serviceRow('Стрижка')).getByRole('button', { name: 'Скрыть' })
    )

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).getByText(
        'Услуга исчезнет из карточки и поиска. Записи, где она была, останутся в истории.'
      )
    ).toBeInTheDocument()

    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Скрыть' })
    )

    expect(await screen.findByText('Услуга скрыта.')).toBeInTheDocument()

    await waitFor(() => {
      expect(within(serviceRow('Стрижка')).getByText('Скрыта')).toBeInTheDocument()
    })
  })

  it('возвращает услугу: POST тем же hairServiceId', async () => {
    const profile = mockOwnProfile()
    let body: unknown = null

    server.use(
      http.post(`${PROFILE_URL}/services`, async ({ request }) => {
        body = await request.json()
        const input = body as { hairServiceId: string }

        profile.setProfile({
          ...profile.current(),
          services: profile.current().services.map((service) =>
            service.hairServiceId === input.hairServiceId
              ? { ...service, isActive: true }
              : service
          ),
        })

        return noContent()
      })
    )

    renderPage()
    await waitForProfile()

    await userEvent.click(
      within(serviceRow('Оформление бороды')).getByRole('button', {
        name: 'Вернуть',
      })
    )

    expect(
      await screen.findByText('Услуга снова в прайсе.')
    ).toBeInTheDocument()

    expect(body).toStrictEqual({
      hairServiceId: testBarberProfile.services[1].hairServiceId,
      price: testBarberProfile.services[1].price,
      durationMinutes: testBarberProfile.services[1].durationMinutes,
    })

    await waitFor(() => {
      expect(
        within(serviceRow('Оформление бороды')).queryByText('Скрыта')
      ).not.toBeInTheDocument()
    })
  })

  it('добавляет и убирает специализацию по справочнику', async () => {
    const profile = mockOwnProfile()
    let addUrl = ''
    let removeUrl = ''

    server.use(
      http.post(
        `${PROFILE_URL}/specializations/:specializationId`,
        ({ request }) => {
          addUrl = request.url

          return noContent()
        }
      ),
      http.delete(
        `${PROFILE_URL}/specializations/:specializationId`,
        ({ request }) => {
          removeUrl = request.url

          return noContent()
        }
      )
    )

    renderPage()
    await waitForProfile()

    const select = screen.getByLabelText('Специализация')

    fireEvent.focus(select)

    expect(
      await screen.findByRole('option', { name: 'Барберинг' })
    ).toBeInTheDocument()

    fireEvent.change(select, {
      target: { value: catalogueSpecialization.id },
    })
    await userEvent.click(screen.getByRole('button', { name: 'Добавить' }))

    expect(
      await screen.findByText('Специализация добавлена.')
    ).toBeInTheDocument()
    expect(new URL(addUrl).pathname).toBe(
      `${PROFILE_URL}/specializations/${catalogueSpecialization.id}`
    )

    await waitFor(() => {
      expect(profile.getCount()).toBe(2)
    })

    await userEvent.click(
      screen.getByRole('button', {
        name: 'Убрать специализацию Барберинг',
      })
    )

    expect(await screen.findByText('Специализация убрана.')).toBeInTheDocument()
    expect(new URL(removeUrl).pathname).toBe(
      `${PROFILE_URL}/specializations/${testBarberProfile.specializations[0].specializationId}`
    )
  })
})

describe('BarberProfilePage: отказы', () => {
  it('ошибка каталога не ломает список и предлагает «Повторить»', async () => {
    let fail = true

    mockOwnProfile()
    server.use(
      http.get('/api/hair-services', () => {
        if (fail) {
          return HttpResponse.json(problemDetails(500), { status: 500 })
        }

        return HttpResponse.json([catalogueHairService])
      })
    )

    renderPage()
    await waitForProfile()

    await userEvent.click(
      screen.getByRole('button', { name: 'Добавить услугу' })
    )

    expect(
      await screen.findByText('Не удалось загрузить каталог услуг.')
    ).toBeInTheDocument()
    // Список услуг остаётся на экране: упал только справочник диалога.
    expect(screen.getAllByText('Стрижка').length).toBeGreaterThan(0)

    fail = false
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }))

    expect(
      await screen.findByRole('option', { name: 'Стрижка' })
    ).toBeInTheDocument()
  })

  it('409 duplicate и 404 notFound: русские тексты, экран жив', async () => {
    mockOwnProfile()

    server.use(
      http.post(`${PROFILE_URL}/services`, () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['barberProfile.addService.duplicate'],
          }),
          { status: 409 }
        )
      ),
      http.put(`${PROFILE_URL}/services/:hairServiceId`, () =>
        HttpResponse.json(
          problemDetails(404, {
            errorCodes: ['barberProfile.updateService.notFound'],
          }),
          { status: 404 }
        )
      )
    )

    renderPage()
    await waitForProfile()

    await userEvent.click(
      screen.getByRole('button', { name: 'Добавить услугу' })
    )

    expect(
      await screen.findByRole('option', { name: 'Стрижка' })
    ).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Услуга'), {
      target: { value: catalogueHairService.id },
    })
    fireEvent.change(screen.getByLabelText('Цена, ₽'), {
      target: { value: '1500' },
    })
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Добавить',
      })
    )

    expect(
      await screen.findByText('Эта услуга уже есть в прайсе.')
    ).toBeInTheDocument()

    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Отмена',
      })
    )

    await userEvent.click(
      within(serviceRow('Стрижка')).getByRole('button', {
        name: 'Изменить цену',
      })
    )

    fireEvent.change(screen.getByLabelText('Цена, ₽'), {
      target: { value: '2000' },
    })
    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Сохранить',
      })
    )

    expect(
      await screen.findByText('Эта услуга уже скрыта. Обновите страницу.')
    ).toBeInTheDocument()

    await userEvent.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Отмена',
      })
    )

    expect(
      screen.getByRole('heading', { level: 1, name: 'Профиль' })
    ).toBeInTheDocument()
  })
})
