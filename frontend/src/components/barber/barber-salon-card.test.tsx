import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { BarberSalonCard } from '@/components/barber/barber-salon-card'
import type { BarberSalonSummary } from '@/lib/barber-salons'

const OWNER_ID = '33333333-3333-4333-8333-333333333333'

const BASE: BarberSalonSummary = {
  salonId: '22222222-2222-4222-8222-222222222222',
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
  ownerUserId: OWNER_ID,
  moderationStatus: 'Active',
  rejectionReason: null,
}

/**
 * Карточка «Моих салонов»: статус модерации словом, причина отказа и действия
 * владельца. Активации здесь быть не должно — видимостью управляет администратор.
 */
describe('BarberSalonCard', () => {
  it('одобренный салон показывает статус и действия правки без активации', () => {
    render(
      <BarberSalonCard
        salon={BASE}
        canEdit
        onEdit={() => {}}
        onManageBarbers={() => {}}
      />
    )

    expect(screen.getByText('Одобрен')).toBeInTheDocument()
    expect(screen.getByText('Основной')).toBeInTheDocument()
    expect(screen.getByText(/Екатеринбург/)).toBeInTheDocument()
    expect(screen.getByText(/ул. Ленина, 1/)).toBeInTheDocument()
    expect(
      screen.queryByText('Салон появится в поиске после проверки администратором.')
    ).not.toBeInTheDocument()

    expect(
      screen.getByRole('button', { name: 'Барберы салона' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Изменить адрес' })
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Изменить контакты' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: /Выключить|Включить/ })
    ).not.toBeInTheDocument()
  })

  it('салон на проверке объясняет, что он появится в поиске после проверки', () => {
    render(
      <BarberSalonCard
        salon={{ ...BASE, moderationStatus: 'Pending', rejectionReason: null }}
        canEdit
        onEdit={() => {}}
        onManageBarbers={() => {}}
      />
    )

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Салон появится в поиске после проверки администратором.'
      )
    ).toBeInTheDocument()
    // Правка разрешена и до решения администратора.
    expect(
      screen.getByRole('button', { name: 'Изменить адрес' })
    ).toBeEnabled()
  })

  it('отклонённый салон показывает причину отказа', () => {
    render(
      <BarberSalonCard
        salon={{
          ...BASE,
          moderationStatus: 'Rejected',
          rejectionReason: 'Не подтверждён адрес салона',
        }}
        canEdit={false}
        onEdit={() => {}}
        onManageBarbers={() => {}}
      />
    )

    expect(screen.getByText('Отклонён')).toBeInTheDocument()
    const reason = screen.getByText(/Причина отказа/)

    expect(reason).toHaveTextContent(
      'Причина отказа: Не подтверждён адрес салона'
    )
  })

  it('без владельца не показывает правку: салон создал администратор', () => {
    render(
      <BarberSalonCard
        salon={{ ...BASE, ownerUserId: null }}
        canEdit={false}
        onEdit={() => {}}
        onManageBarbers={() => {}}
      />
    )

    expect(screen.getByText('Одобрен')).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Барберы салона' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Изменить адрес' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Изменить контакты' })
    ).not.toBeInTheDocument()
  })

  it('каждая кнопка правки просит свою группу полей', async () => {
    const user = userEvent.setup()
    const onEdit = vi.fn()

    render(
      <BarberSalonCard
        salon={BASE}
        canEdit
        onEdit={onEdit}
        onManageBarbers={() => {}}
      />
    )

    const card = screen
      .getByText(BASE.name)
      .closest<HTMLElement>('[data-slot="card"]')!

    await user.click(
      within(card).getByRole('button', { name: 'Изменить адрес' })
    )
    await user.click(
      within(card).getByRole('button', { name: 'Изменить контакты' })
    )

    expect(onEdit.mock.calls).toStrictEqual([['location'], ['contacts']])
  })

  it('кнопка состава салона передаёт управление родителю', async () => {
    const user = userEvent.setup()
    const onManageBarbers = vi.fn()

    render(
      <BarberSalonCard
        salon={BASE}
        canEdit
        onEdit={() => {}}
        onManageBarbers={onManageBarbers}
      />
    )

    await user.click(screen.getByRole('button', { name: 'Барберы салона' }))

    expect(onManageBarbers).toHaveBeenCalledTimes(1)
  })
})
