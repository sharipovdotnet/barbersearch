import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SalonDialogForm } from '@/components/salon/salon-form'
import type { AdminSalon } from '@/lib/admin-catalogue'
import type { SalonFormSalon } from '@/lib/salon-form'
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
  salon: (SalonFormSalon & { id?: string }) | null = null
) {
  return render(
    <SalonDialogForm
      scope={scope}
      mode={mode}
      salon={salon}
      salonId={salon?.id ?? null}
      onClose={() => {}}
      onSaved={() => {}}
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
