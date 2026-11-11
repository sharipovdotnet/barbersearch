import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { BarberHero } from '@/components/barber/barber-hero'
import type { BarberProfile } from '@/lib/catalog'
import { testBarberProfile } from '@/test/msw/handlers'
import { stubImageLoading } from '@/test/image'

function profileWith(avatarUrl: string | null): BarberProfile {
  return { ...testBarberProfile, avatarUrl }
}

describe('BarberHero: аватар', () => {
  it('показывает изображение профиля, когда avatarUrl задан', async () => {
    const avatarUrl =
      '/api/media/avatars/9e000000-0000-4000-8000-000000000115/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

    stubImageLoading()

    const { container } = render(
      <BarberHero profile={profileWith(avatarUrl)} />
    )

    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', avatarUrl)
    })

    // Инициалы остаются fallback'ом, а не вторым содержимым рядом с фото.
    expect(screen.queryByText('ИП')).not.toBeInTheDocument()
  })

  it('без аватара показывает инициалы и не рендерит изображение', () => {
    render(<BarberHero profile={profileWith(null)} />)

    expect(screen.getByText('ИП')).toBeInTheDocument()
    expect(document.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })
})
