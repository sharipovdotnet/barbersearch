import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { BarberServices } from '@/components/barber/barber-services'
import type { BarberProfile, BarberProfileService } from '@/lib/catalog'
import { testBarberProfile } from '@/test/msw/handlers'

/**
 * Публичный прайс показывает обе подписи услуги: категорию и специализацию.
 * Название услуги в тесте отличается от подписи категории, иначе `getByText`
 * не смог бы отличить строку от бейджа.
 */
function profileWithService(): BarberProfile {
  return {
    ...testBarberProfile,
    services: [{ ...testBarberProfile.services[0], name: 'Фейд' }],
  }
}

describe('BarberServices: подписи услуги', () => {
  it('показывает категорию и специализацию рядом с длительностью', () => {
    render(<BarberServices profile={profileWithService()} />)

    const row = screen.getByRole('listitem')

    expect(within(row).getByText('Фейд')).toBeInTheDocument()
    expect(within(row).getByText('Стрижка')).toBeInTheDocument()
    expect(within(row).getByText('Барберинг')).toBeInTheDocument()
  })

  it('не показывает скрытую услугу', () => {
    render(<BarberServices profile={profileWithService()} />)

    expect(screen.queryByText('Оформление бороды')).not.toBeInTheDocument()
  })

  it('сортирует строки: категория → специализация → название', () => {
    const base = testBarberProfile.services[0]
    const services: BarberProfileService[] = [
      {
        ...base,
        barberServiceId: 'service-stylist',
        name: 'Вечерняя укладка',
        category: 'Styling',
        specializationName: 'Стилист',
      },
      {
        ...base,
        barberServiceId: 'service-beard',
        name: 'Оформление бороды',
        category: 'Beard',
        specializationName: 'Борода',
      },
      {
        ...base,
        barberServiceId: 'service-fade',
        name: 'Фейд',
        specializationName: 'Барберинг',
      },
      {
        ...base,
        barberServiceId: 'service-classic',
        name: 'Классика',
        specializationName: 'Барберинг',
      },
    ]

    render(<BarberServices profile={{ ...testBarberProfile, services }} />)

    const names = screen
      .getAllByRole('listitem')
      .map((row) => row.querySelector('p')?.textContent)

    expect(names).toStrictEqual([
      'Классика',
      'Фейд',
      'Оформление бороды',
      'Вечерняя укладка',
    ])
  })
})
