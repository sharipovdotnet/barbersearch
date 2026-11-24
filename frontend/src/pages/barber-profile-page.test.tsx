import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  SessionContext,
  type SessionContextValue,
} from '@/hooks/use-session'
import type { BarberProfile } from '@/lib/catalog'
import { preparePortfolioImage } from '@/lib/image-prep'
import type { PortfolioPhoto } from '@/lib/portfolio-photos'
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

vi.mock('@/lib/image-prep', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/image-prep')>()

  return {
    ...actual,
    // jsdom не рисует canvas: подготовку файла подменяем, транспорт остаётся настоящим.
    preparePortfolioImage: vi.fn(async (file: File) => file),
  }
})

const PROFILE_URL = `/api/barber-profiles/${testBarberProfile.id}`
const PHOTOS_URL = `${PROFILE_URL}/photos`

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

/** Фотография портфолио в фикстурах страницы. */
function portfolioPhoto(
  overrides: Partial<PortfolioPhoto> = {}
): PortfolioPhoto {
  return {
    id: 'eeeeeeee-1111-4111-8111-111111111111',
    url: `/api/media/portfolio/${testBarberProfile.id}/eeeeeeee111141118111111111111111.jpg`,
    thumbnailUrl: null,
    description: 'Стрижка',
    sortOrder: 0,
    isPublished: true,
    isBlocked: false,
    moderationStatus: 'Approved',
    rejectionReason: null,
    ...overrides,
  }
}

/** Профиль с портфолио: остальные поля — из общей фикстуры MSW. */
function profileWith(photos: PortfolioPhoto[]): BarberProfile {
  return { ...cloneProfile(testBarberProfile), photos }
}

function photoFile(): File {
  return new File(['photo-bytes'], 'IMG_0001.jpeg', { type: 'image/jpeg' })
}

describe('BarberProfilePage: фотографии', () => {
  beforeEach(() => {
    // jsdom не умеет object URL: превью получает стабильную заглушку.
    URL.createObjectURL = vi.fn(() => 'blob:preview')
    URL.revokeObjectURL = vi.fn()
  })

  it('пустое портфолио: секция между карточкой и услугами', async () => {
    mockOwnProfile()

    renderPage()
    await waitForProfile()

    expect(
      screen.getByRole('heading', { level: 2, name: 'Фотографии' })
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Фотографий нет. Добавьте работы, чтобы клиенты видели ваш стиль.'
      )
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Добавить фото' })
    ).toBeEnabled()
  })

  it('две работы: подписи, бейдж скрытой и стрелки порядка', async () => {
    mockOwnProfile(
      profileWith([
        portfolioPhoto(),
        portfolioPhoto({
          id: 'eeeeeeee-2222-4222-8222-222222222222',
          description: 'Борода',
          sortOrder: 1,
          isPublished: false,
        }),
      ])
    )

    renderPage()
    await waitForProfile()

    expect(screen.getByAltText('Стрижка')).toBeInTheDocument()
    expect(screen.getByAltText('Борода')).toBeInTheDocument()
    expect(screen.getByText('Скрыто')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Ниже: Стрижка' })
    ).toBeEnabled()
    expect(
      screen.getByRole('button', { name: 'Вернуть: Борода' })
    ).toBeInTheDocument()
  })

  it('загружает фото: подготовка файла, POST, объявление и перечитывание', async () => {
    const user = userEvent.setup()
    const profile = mockOwnProfile()
    let uploads = 0

    server.use(
      http.post(`${PHOTOS_URL}/upload`, () => {
        uploads += 1

        return HttpResponse.json(
          { id: portfolioPhoto().id },
          { status: 201 }
        )
      })
    )

    renderPage()
    await waitForProfile()

    await user.click(screen.getByRole('button', { name: 'Добавить фото' }))
    await user.upload(screen.getByLabelText('Файл'), photoFile())
    await user.type(screen.getByLabelText('Подпись'), 'Стрижка')
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Загрузить',
      })
    )

    expect(await screen.findByText('Фото добавлено.')).toBeInTheDocument()
    expect(uploads).toBe(1)
    expect(vi.mocked(preparePortfolioImage)).toHaveBeenCalledTimes(1)

    await waitFor(() => {
      expect(profile.getCount()).toBe(2)
    })
  })

  it('десять фото: кнопка загрузки disabled и текст лимита', async () => {
    mockOwnProfile(
      profileWith(
        Array.from({ length: 10 }, (_item, index) =>
          portfolioPhoto({
            id: `eeeeeeee-3333-4333-8333-00000000000${index}`,
            description: `Работа ${index + 1}`,
            sortOrder: index,
          })
        )
      )
    )

    renderPage()
    await waitForProfile()

    expect(
      screen.getByRole('button', { name: 'Добавить фото' })
    ).toBeDisabled()
    expect(
      screen.getByText(
        'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.'
      )
    ).toBeInTheDocument()
  })

  it('удаляет фото через alertdialog и перечитывает профиль', async () => {
    const user = userEvent.setup()
    const profile = mockOwnProfile(profileWith([portfolioPhoto()]))

    server.use(
      http.delete(`${PHOTOS_URL}/:photoId`, () => noContent())
    )

    renderPage()
    await waitForProfile()

    await user.click(screen.getByRole('button', { name: 'Удалить: Стрижка' }))

    const dialog = screen.getByRole('alertdialog')

    expect(
      within(dialog).getByText(
        'Фото исчезнет из карточки и поиска. Это необратимо.'
      )
    ).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Удалить' }))

    expect(await screen.findByText('Фото удалено.')).toBeInTheDocument()

    await waitFor(() => {
      expect(profile.getCount()).toBe(2)
    })
  })

  it('скрывает и возвращает фото: DELETE и POST публикации', async () => {
    const user = userEvent.setup()
    const base = profileWith([portfolioPhoto()])
    const profile = mockOwnProfile(base)
    let deleted = 0
    let published = 0

    server.use(
      http.delete(`${PHOTOS_URL}/:photoId/publication`, () => {
        deleted += 1
        profile.setProfile(
          profileWith([
            portfolioPhoto({ isPublished: false }),
          ])
        )

        return noContent()
      }),
      http.post(`${PHOTOS_URL}/:photoId/publication`, () => {
        published += 1
        profile.setProfile(base)

        return noContent()
      })
    )

    renderPage()
    await waitForProfile()

    await user.click(screen.getByRole('button', { name: 'Скрыть: Стрижка' }))

    expect(await screen.findByText('Фото скрыто.')).toBeInTheDocument()

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Вернуть: Стрижка' })
      ).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: 'Вернуть: Стрижка' }))

    expect(
      await screen.findByText('Фото снова в карточке.')
    ).toBeInTheDocument()
    expect(deleted).toBe(1)
    expect(published).toBe(1)
  })

  it('заблокированное администратором фото: нет «Вернуть»', async () => {
    mockOwnProfile(profileWith([portfolioPhoto({ isBlocked: true })]))

    renderPage()
    await waitForProfile()

    expect(screen.getByText('Скрыто администратором')).toBeInTheDocument()

    const section = screen.getByRole('region', { name: 'Фотографии' })

    expect(
      within(section).queryByRole('button', { name: /Вернуть/ })
    ).not.toBeInTheDocument()
    expect(
      within(section).getByRole('button', { name: 'Удалить: Стрижка' })
    ).toBeInTheDocument()
  })

  it('409 лимита и 400 типа файла объясняются в диалоге', async () => {
    const user = userEvent.setup()
    let uploads = false
    let failWith = ''

    server.use(
      http.post(`${PHOTOS_URL}/upload`, () => {
        uploads = true

        if (failWith === 'limit') {
          return HttpResponse.json(
            problemDetails(409, {
              errorCodes: ['barberProfile.addPhoto.limit'],
            }),
            { status: 409 }
          )
        }

        return HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['barberProfile.uploadPhoto.unsupportedType'],
          }),
          { status: 400 }
        )
      })
    )

    failWith = 'limit'
    mockOwnProfile()
    renderPage()
    await waitForProfile()

    await user.click(screen.getByRole('button', { name: 'Добавить фото' }))
    await user.upload(screen.getByLabelText('Файл'), photoFile())
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Загрузить',
      })
    )

    expect(
      await screen.findByText(
        'В портфолио уже 10 фото. Удалите одно, чтобы добавить новое.'
      )
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    failWith = 'type'
    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Загрузить',
      })
    )

    expect(
      await screen.findByText('Поддерживаются только JPEG, PNG и WebP.')
    ).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(uploads).toBe(true)
  })
})
