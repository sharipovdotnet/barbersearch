import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { BarberServices } from '@/components/barber/barber-services'
import type { BarberProfile } from '@/lib/catalog'
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
})
