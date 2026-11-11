import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { BarberIdentity } from '@/components/admin/barber-list'
import type { AdminBarber } from '@/lib/admin-barbers'
import { stubImageLoading } from '@/test/image'

/**
 * Строка барбера: фото владельца профиля, инициалы без фото и бейдж
 * «Фото скрыто». Аватар принадлежит пользователю, но показывается в админской
 * строке барбера, потому что карточка профиля ведёт к тому же владельцу.
 */

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000001/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

const BARBER: AdminBarber = {
  id: '9e800000-0000-4000-8000-000000000001',
  displayName: 'Артём Соколов',
  email: 'artem@barbersearch.test',
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  grade: 'ArtDirector',
  rating: 4.5,
  reviewsCount: 29,
  isActive: true,
  isVerified: true,
  createdAt: '2026-09-28T15:12:00+00:00',
  updatedAt: '2026-11-02T10:53:00+00:00',
}

describe('BarberIdentity: аватар', () => {
  it('показывает изображение, когда у владельца есть фото', async () => {
    stubImageLoading()

    const { container } = render(
      <BarberIdentity barber={{ ...BARBER, avatarUrl: AVATAR_URL }} />
    )

    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })
    expect(container.querySelector('[data-slot="avatar-image"]')).toHaveAttribute(
      'alt',
      ''
    )
    expect(screen.getByText('Артём Соколов')).toBeInTheDocument()
  })

  it('без фото показывает инициалы', () => {
    const { container } = render(<BarberIdentity barber={BARBER} />)

    expect(screen.getByText('АС')).toBeInTheDocument()
    expect(container.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })

  it('при скрытом фото показывает бейдж «Фото скрыто»', async () => {
    stubImageLoading()

    render(
      <BarberIdentity
        barber={{ ...BARBER, avatarUrl: AVATAR_URL, isAvatarBlocked: true }}
      />
    )

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
  })

  it('у кадра «На проверке» показывает бейдж рядом с именем', () => {
    render(
      <BarberIdentity barber={{ ...BARBER, avatarModerationStatus: 'Pending' }} />
    )

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(screen.queryByText('Фото скрыто')).not.toBeInTheDocument()
  })

  it('у отклонённого кадра показывает «Отклонено»', () => {
    render(
      <BarberIdentity
        barber={{ ...BARBER, avatarModerationStatus: 'Rejected' }}
      />
    )

    expect(screen.getByText('Отклонено')).toBeInTheDocument()
  })

  it('приоритет бейджей: скрыто важнее отклонения и проверки', () => {
    render(
      <BarberIdentity
        barber={{
          ...BARBER,
          isAvatarBlocked: true,
          avatarModerationStatus: 'Rejected',
        }}
      />
    )

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
    expect(screen.queryByText('Отклонено')).not.toBeInTheDocument()
    expect(screen.queryByText('На проверке')).not.toBeInTheDocument()
  })
})
