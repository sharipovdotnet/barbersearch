import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { BarberSpecializations } from '@/components/barber/barber-specializations'
import type { BarberProfile } from '@/lib/catalog'
import { testBarberProfile } from '@/test/msw/handlers'

const DESCRIPTION = 'Мужские стрижки, борода и бритьё.'

/**
 * Две специализации: с описанием и без. Оба состояния видны в одном рендере,
 * поэтому тесты не собирают профиль заново ради одного бейджа.
 */
function profileWithSpecializations(): BarberProfile {
  return {
    ...testBarberProfile,
    specializations: [
      {
        ...testBarberProfile.specializations[0],
        id: 'specialization-barbering',
        name: 'Барберинг',
        description: DESCRIPTION,
      },
      {
        ...testBarberProfile.specializations[0],
        id: 'specialization-beard',
        name: 'Борода',
        description: null,
      },
    ],
  }
}

describe('BarberSpecializations: бейджи и подсказки', () => {
  it('показывает названия специализаций бейджами', () => {
    render(<BarberSpecializations profile={profileWithSpecializations()} />)

    const section = screen.getByRole('region', { name: 'Специализации' })

    expect(within(section).getByText('Барберинг')).toBeInTheDocument()
    expect(within(section).getByText('Борода')).toBeInTheDocument()
  })

  it('открывает описание при наведении на бейдж', async () => {
    const user = userEvent.setup()

    render(<BarberSpecializations profile={profileWithSpecializations()} />)

    const trigger = screen.getByRole('button', { name: 'Барберинг' })

    // До наведения подсказки нет: бейдж не обрастает скрытым текстом.
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()

    await user.hover(trigger)

    expect(await screen.findByRole('tooltip')).toHaveTextContent(DESCRIPTION)
  })

  it('открывает описание и на клавиатурном фокусе', async () => {
    const user = userEvent.setup()

    render(<BarberSpecializations profile={profileWithSpecializations()} />)

    // Единственный фокусируемый элемент блока — бейдж с описанием.
    await user.tab()

    const trigger = screen.getByRole('button', { name: 'Барберинг' })

    expect(trigger).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent(DESCRIPTION)
  })

  it('без описания показывает название, но не делает бейдж подсказкой', async () => {
    const user = userEvent.setup()

    render(<BarberSpecializations profile={profileWithSpecializations()} />)

    const badge = screen.getByText('Борода')

    expect(
      screen.queryByRole('button', { name: 'Борода' })
    ).not.toBeInTheDocument()

    await user.hover(badge)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('не рендерит блок без специализаций', () => {
    render(
      <BarberSpecializations
        profile={{ ...testBarberProfile, specializations: [] }}
      />
    )

    expect(
      screen.queryByRole('region', { name: 'Специализации' })
    ).not.toBeInTheDocument()
  })
})
