import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SalonDialogForm } from '@/components/salon/salon-form'
import type { AdminSalon } from '@/lib/admin-catalogue'
import type { AddressSuggestion, CitySuggestion } from '@/lib/address-suggestions'
import type { SalonFormSalon } from '@/lib/salon-form'
import { problemDetails } from '@/test/msw/handlers'
import { recordRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const ADMIN_SALON: AdminSalon = {
  id: '9e400000-0000-4000-8000-000000000001',
  name: 'E2E Studio',
  address: 'ул. Тестовая, 1',
  city: 'Москва',
  postalCode: '101000',
  country: 'Russia',
  timeZoneId: 'Europe/Moscow',
  latitude: 55.7558,
  longitude: 37.6173,
  phoneNumber: '+70000000009',
  websiteUrl: null,
  isActive: true,
  moderationStatus: 'Active',
  kind: 'Salon',
  isListed: true,
  rejectionReason: null,
  reviewedAt: null,
  ownerEmail: null,
  usageCount: 13,
}

function renderForm(
  scope: 'admin' | 'owner',
  mode: 'create' | 'edit',
  salon: (SalonFormSalon & { id?: string }) | null = null,
  onSaved: (message: string) => void = () => {}
) {
  return render(
    <SalonDialogForm
      scope={scope}
      mode={mode}
      salon={salon}
      salonId={salon?.id ?? null}
      onClose={() => {}}
      onSaved={onSaved}
    />
  )
}

/**
 * Общая форма в двух режимах: админский каталог и кабинет барбера.
 *
 * Проверяем различия, которые нельзя вынести в чистую валидацию: подсказка о
 * неизменном названии, координаты вместо карты, скрытый почтовый индекс у барбера
 * и клиентские ошибки до запроса.
 */
describe('SalonDialogForm: режимы', () => {
  it('админское создание показывает все поля, включая почтовый индекс', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    renderForm('admin', 'create')

    const dialog = screen.getByRole('dialog')

    expect(within(dialog).getByLabelText('Название')).toBeEnabled()
    expect(within(dialog).getByLabelText('Почтовый индекс')).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Создать' }))

    expect(within(dialog).getByText('Введите название.')).toBeInTheDocument()
    expect(within(dialog).getByText('Введите адрес.')).toBeInTheDocument()
    expect(requests).toStrictEqual([])
  })

  it('правка: название read-only и объяснено, почтовый индекс на месте', () => {
    renderForm('admin', 'edit', ADMIN_SALON)

    const dialog = screen.getByRole('dialog')
    const name = within(dialog).getByLabelText('Название')

    expect(name).toBeDisabled()
    expect(name).toHaveValue(ADMIN_SALON.name)
    expect(
      within(dialog).getByText('Название задаётся при создании.')
    ).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue(
      '101000'
    )
    expect(
      within(dialog).getByRole('button', { name: 'Сохранить' })
    ).toBeInTheDocument()
  })

  it('барбер: предупреждение о названии, подсказка о координатах и нет индекса', () => {
    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')

    expect(
      within(dialog).getByText(
        'Название нельзя изменить после создания салона.'
      )
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText(/Скопируйте координаты из карт/)
    ).toBeInTheDocument()
    expect(
      within(dialog).queryByLabelText('Почтовый индекс')
    ).not.toBeInTheDocument()
    expect(
      within(dialog).getByRole('button', { name: 'Отправить на проверку' })
    ).toBeInTheDocument()
  })

  it('барбер: правка предзаполняет страну, индекс, координаты и контакты', () => {
    renderForm('owner', 'edit', ADMIN_SALON)

    const dialog = screen.getByRole('dialog')

    expect(within(dialog).getByLabelText('Страна')).toHaveValue(
      ADMIN_SALON.country
    )
    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue(
      ADMIN_SALON.postalCode
    )
    expect(within(dialog).getByLabelText('Широта')).toHaveValue(
      String(ADMIN_SALON.latitude)
    )
    expect(within(dialog).getByLabelText('Долгота')).toHaveValue(
      String(ADMIN_SALON.longitude)
    )
    expect(within(dialog).getByLabelText('Телефон')).toHaveValue(
      ADMIN_SALON.phoneNumber
    )
    expect(within(dialog).getByLabelText('Сайт')).toHaveValue('')
    // Никакого предупреждения о повторном вводе: значения пришли из DTO.
    expect(within(dialog).queryByText(/не отдаёт/)).not.toBeInTheDocument()
    expect(
      within(dialog).getByRole('button', { name: 'Сохранить' })
    ).toBeInTheDocument()
  })

  it('пустая форма барбера не отправляет запрос', async () => {
    const user = userEvent.setup()
    const requests = recordRequests()

    renderForm('owner', 'create')

    await user.click(
      within(screen.getByRole('dialog')).getByRole('button', {
        name: 'Отправить на проверку',
      })
    )

    expect(requests).toStrictEqual([])
  })
})

/** Заполнить обязательные поля формы, не трогая тип места. */
async function fillMinimalSalonForm(
  user: ReturnType<typeof userEvent.setup>,
  dialog: HTMLElement
) {
  await user.type(within(dialog).getByLabelText('Название'), 'Место приёма')
  await user.type(within(dialog).getByLabelText('Адрес'), 'ул. Ленина, 1')
  await user.type(within(dialog).getByLabelText('Город'), 'Екатеринбург')
  await user.type(within(dialog).getByLabelText('Страна'), 'Россия')
  await user.type(within(dialog).getByLabelText('Широта'), '56.8389')
  await user.type(within(dialog).getByLabelText('Долгота'), '60.6057')
}

describe('SalonDialogForm: тип места', () => {
  it('по умолчанию барбершоп, а показ в каталоге включён', () => {
    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')
    const salonRadio = within(dialog).getByRole('radio', {
      name: /Барбершоп/,
    })
    const listed = within(dialog).getByRole('checkbox', {
      name: 'Показывать в каталоге',
    })

    expect(salonRadio).toBeChecked()
    expect(listed).toBeChecked()
    expect(listed).toBeEnabled()
  })

  it('выбор «На дому» выключает и блокирует каталог, не теряя поля', async () => {
    const user = userEvent.setup()

    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')

    await user.type(within(dialog).getByLabelText('Название'), 'Дом на Ленина')
    await user.click(within(dialog).getByRole('radio', { name: /На дому/ }))

    expect(within(dialog).getByLabelText('Название')).toHaveValue(
      'Дом на Ленина'
    )

    const listed = within(dialog).getByRole('checkbox', {
      name: 'Показывать в каталоге',
    })

    expect(listed).not.toBeChecked()
    expect(listed).toHaveAttribute('aria-disabled', 'true')
    expect(
      within(dialog).getByText(/Скрытое место не попадает в каталог/)
    ).toBeInTheDocument()
  })

  it('скрытое место уходит с kind и isListed=false и без проверки', async () => {
    const user = userEvent.setup()
    const bodies: unknown[] = []

    server.use(
      http.post('/api/salons/own', async ({ request }) => {
        bodies.push(await request.json())

        return HttpResponse.json(
          { id: '22222222-2222-4222-8222-222222222222' },
          { status: 201 }
        )
      })
    )

    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')

    await fillMinimalSalonForm(user, dialog)
    await user.click(within(dialog).getByRole('radio', { name: /На дому/ }))
    await user.click(
      within(dialog).getByRole('button', { name: 'Создать место' })
    )

    await waitFor(() => {
      expect(bodies).toHaveLength(1)
    })

    expect(bodies[0]).toMatchObject({ kind: 'Home', isListed: false })
  })

  it('кабинет можно оставить в каталоге, студию — тоже', async () => {
    const user = userEvent.setup()
    const bodies: unknown[] = []

    server.use(
      http.post('/api/salons/own', async ({ request }) => {
        bodies.push(await request.json())

        return HttpResponse.json(
          { id: '22222222-2222-4222-8222-222222222222' },
          { status: 201 }
        )
      })
    )

    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')

    await fillMinimalSalonForm(user, dialog)
    await user.click(within(dialog).getByRole('radio', { name: /Кабинет/ }))

    const listed = within(dialog).getByRole('checkbox', {
      name: 'Показывать в каталоге',
    })

    expect(listed).not.toBeChecked()
    await user.click(listed)

    await user.click(
      within(dialog).getByRole('button', { name: 'Отправить на проверку' })
    )

    await waitFor(() => {
      expect(bodies).toHaveLength(1)
    })

    expect(bodies[0]).toMatchObject({
      kind: 'SoloStudio',
      isListed: true,
    })
  })
})

const CITY_SUGGESTION: CitySuggestion = {
  providerId: 'p-1',
  display: 'г. Екатеринбург',
  city: 'Екатеринбург',
  region: 'Свердловская обл.',
  country: 'Россия',
  latitude: 56.8389,
  longitude: 60.5975,
}

const ADDRESS_SUGGESTION: AddressSuggestion = {
  providerId: 'a-1',
  display: 'г. Екатеринбург, ул. Ленина, д. 1',
  address: 'ул. Ленина, 1',
  city: 'Екатеринбург',
  region: 'Свердловская обл.',
  country: 'Россия',
  postalCode: '620000',
  latitude: 56.8389,
  longitude: 60.5975,
  timeZoneId: null,
}

function serveSuggestions(
  addresses: readonly AddressSuggestion[] = [ADDRESS_SUGGESTION]
): void {
  server.use(
    http.get('/api/addresses/cities', () =>
      HttpResponse.json([CITY_SUGGESTION])
    ),
    http.get('/api/addresses/suggest', () => HttpResponse.json(addresses))
  )
}

type User = ReturnType<typeof userEvent.setup>

async function pickCity(user: User, dialog: HTMLElement): Promise<void> {
  await user.type(within(dialog).getByLabelText('Город'), 'Екат')
  await user.click(await screen.findByRole('option', { name: 'г. Екатеринбург' }))
}

async function pickAddress(
  user: User,
  dialog: HTMLElement,
  option = 'г. Екатеринбург, ул. Ленина, д. 1'
): Promise<void> {
  await user.type(
    within(dialog).getByLabelText('Адрес', { exact: true }),
    'ул. Лен'
  )
  await user.click(await screen.findByRole('option', { name: option }))
}

function captureCreatedSalon(): unknown[] {
  const bodies: unknown[] = []

  server.use(
    http.post('/api/salons', async ({ request }) => {
      bodies.push(await request.json())

      return HttpResponse.json(
        { id: '22222222-2222-4222-8222-222222222222' },
        { status: 201 }
      )
    }),
    http.post('/api/salons/own', async ({ request }) => {
      bodies.push(await request.json())

      return HttpResponse.json(
        { id: '33333333-3333-4333-8333-333333333333' },
        { status: 201 }
      )
    })
  )

  return bodies
}

/**
 * Подсказки города и адреса в живой форме.
 *
 * `providerId` подсказки — токен провайдера: он живёт в состоянии диалога и не должен
 * попадать ни в `POST`, ни в `PUT`. Снимок подсказанных значений решает, что стирать
 * при смене города и правке адреса.
 */
describe('SalonDialogForm: подсказки адреса', () => {
  it('выбор города и адреса заполняет поля и не отправляет providerId', async () => {
    const user = userEvent.setup()
    const bodies = captureCreatedSalon()

    serveSuggestions()
    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')

    await user.type(within(dialog).getByLabelText('Название'), 'Место приёма')
    await pickCity(user, dialog)

    expect(within(dialog).getByLabelText('Город')).toHaveValue('Екатеринбург')
    expect(within(dialog).getByLabelText('Страна')).toHaveValue('Россия')

    await pickAddress(user, dialog)

    await user.click(
      within(dialog).getByRole('button', { name: 'Отправить на проверку' })
    )

    await waitFor(() => {
      expect(bodies).toHaveLength(1)
    })

    expect(bodies[0]).toMatchObject({
      city: 'Екатеринбург',
      country: 'Россия',
      address: 'ул. Ленина, 1',
      latitude: 56.8389,
      longitude: 60.5975,
      // У барбера при создании индекс скрыт — он не должен появиться в теле.
      postalCode: null,
    })
    expect(JSON.stringify(bodies[0])).not.toContain('p-1')
    expect(JSON.stringify(bodies[0])).not.toContain('a-1')
  })

  it('ввод города без выбора: город уходит в город, а адрес ищется без providerId', async () => {
    const user = userEvent.setup()
    const bodies = captureCreatedSalon()
    const suggestUrls: string[] = []

    server.use(
      http.get('/api/addresses/cities', () => HttpResponse.json([])),
      http.get('/api/addresses/suggest', ({ request }) => {
        suggestUrls.push(request.url)

        return HttpResponse.json([])
      })
    )

    renderForm('owner', 'create')

    const dialog = screen.getByRole('dialog')

    await user.type(within(dialog).getByLabelText('Название'), 'Место приёма')
    await user.type(within(dialog).getByLabelText('Город'), 'Екат')
    await user.type(
      within(dialog).getByLabelText('Адрес', { exact: true }),
      'ул. Ленина, 1'
    )

    await waitFor(() => {
      expect(suggestUrls).toHaveLength(1)
    })

    const suggestUrl = new URL(suggestUrls[0] ?? '', 'http://localhost')

    expect(suggestUrl.searchParams.get('cityProviderId')).toBeNull()
    expect(suggestUrl.searchParams.get('city')).toBe('Екат')

    await user.type(within(dialog).getByLabelText('Страна'), 'Россия')
    await user.type(within(dialog).getByLabelText('Широта'), '56.8389')
    await user.type(within(dialog).getByLabelText('Долгота'), '60.5975')
    await user.click(
      within(dialog).getByRole('button', { name: 'Отправить на проверку' })
    )

    await waitFor(() => {
      expect(bodies).toHaveLength(1)
    })

    expect(bodies[0]).toMatchObject({
      city: 'Екат',
      address: 'ул. Ленина, 1',
    })
  })

  it('выбор адреса заполняет индекс и координаты, часовой пояс остаётся ручным', async () => {
    const user = userEvent.setup()
    const bodies = captureCreatedSalon()

    serveSuggestions()
    renderForm('admin', 'create')

    const dialog = screen.getByRole('dialog')
    const timeZone = within(dialog).getByLabelText('Часовой пояс')

    expect(timeZone).toBeInstanceOf(HTMLSelectElement)

    const timeZoneBefore = (timeZone as HTMLSelectElement).value

    await user.type(within(dialog).getByLabelText('Название'), 'Салон')
    await pickCity(user, dialog)
    await pickAddress(user, dialog)

    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue(
      '620000'
    )
    expect(within(dialog).getByLabelText('Широта')).toHaveValue('56.8389')
    expect(within(dialog).getByLabelText('Долгота')).toHaveValue('60.5975')
    expect(timeZone).toHaveValue(timeZoneBefore)

    await user.click(within(dialog).getByRole('button', { name: 'Создать' }))

    await waitFor(() => {
      expect(bodies).toHaveLength(1)
    })

    expect(bodies[0]).toMatchObject({
      address: 'ул. Ленина, 1',
      postalCode: '620000',
      latitude: 56.8389,
      longitude: 60.5975,
      timeZoneId: timeZoneBefore,
    })
    expect(Object.keys(bodies[0] as Record<string, unknown>)).not.toContain(
      'providerId'
    )
  })

  it('правка адреса после выбора очищает индекс и координаты', async () => {
    const user = userEvent.setup()

    serveSuggestions()
    renderForm('admin', 'create')

    const dialog = screen.getByRole('dialog')

    await pickCity(user, dialog)
    await pickAddress(user, dialog)

    const address = within(dialog).getByLabelText('Адрес', { exact: true })

    await user.clear(address)
    await user.type(address, 'ул. Другая, 5')

    expect(address).toHaveValue('ул. Другая, 5')
    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue('')
    expect(within(dialog).getByLabelText('Широта')).toHaveValue('')
    expect(within(dialog).getByLabelText('Долгота')).toHaveValue('')
  })

  it('ручные координаты не затираются подсказкой без координат', async () => {
    const user = userEvent.setup()

    serveSuggestions([
      { ...ADDRESS_SUGGESTION, latitude: null, longitude: null },
    ])
    renderForm('admin', 'create')

    const dialog = screen.getByRole('dialog')

    await pickCity(user, dialog)
    await user.type(within(dialog).getByLabelText('Широта'), '56.1')
    await user.type(within(dialog).getByLabelText('Долгота'), '60.1')
    await pickAddress(user, dialog)

    expect(within(dialog).getByLabelText('Широта')).toHaveValue('56.1')
    expect(within(dialog).getByLabelText('Долгота')).toHaveValue('60.1')
    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue(
      '620000'
    )
  })

  it('смена города очищает подсказанные адресные поля', async () => {
    const user = userEvent.setup()

    serveSuggestions()
    renderForm('admin', 'create')

    const dialog = screen.getByRole('dialog')

    await pickCity(user, dialog)
    await pickAddress(user, dialog)

    const city = within(dialog).getByLabelText('Город')

    await user.clear(city)
    await user.type(city, 'Москва')

    expect(within(dialog).getByLabelText('Адрес', { exact: true })).toHaveValue(
      ''
    )
    expect(within(dialog).getByLabelText('Почтовый индекс')).toHaveValue('')
    expect(within(dialog).getByLabelText('Широта')).toHaveValue('')
    expect(within(dialog).getByLabelText('Долгота')).toHaveValue('')
  })

  it('409 от подсказок: форма сохраняется без ошибок полей и общего alert', async () => {
    const user = userEvent.setup()
    const bodies = captureCreatedSalon()
    const saved: string[] = []

    server.use(
      http.get('/api/addresses/cities', () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['addressSuggestions.unavailable'],
          }),
          { status: 409 }
        )
      ),
      http.get('/api/addresses/suggest', () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['addressSuggestions.unavailable'],
          }),
          { status: 409 }
        )
      )
    )

    renderForm('owner', 'create', null, (message) => saved.push(message))

    const dialog = screen.getByRole('dialog')

    await user.type(within(dialog).getByLabelText('Название'), 'Место приёма')
    await user.type(within(dialog).getByLabelText('Город'), 'Екатеринбург')
    await user.type(
      within(dialog).getByLabelText('Адрес', { exact: true }),
      'ул. Ленина, 1'
    )
    await user.type(within(dialog).getByLabelText('Страна'), 'Россия')
    await user.type(within(dialog).getByLabelText('Широта'), '56.8389')
    await user.type(within(dialog).getByLabelText('Долгота'), '60.5975')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    await user.click(
      within(dialog).getByRole('button', { name: 'Отправить на проверку' })
    )

    await waitFor(() => {
      expect(saved).toHaveLength(1)
    })

    expect(bodies).toHaveLength(1)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(within(dialog).queryByText('Введите город.')).not.toBeInTheDocument()
  })
})
