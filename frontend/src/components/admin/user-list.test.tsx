import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { UserIdentity } from '@/components/admin/user-list'
import type { PhotoModerationStatus } from '@/lib/photo-moderation'
import { stubImageLoading } from '@/test/image'

/**
 * Строка пользователя: фото владельца, инициалы без фото и текстовый бейдж
 * «Фото скрыто». Админ видит сырой URL и при блокировке — иначе он не смог бы
 * решить, возвращать кадр или нет.
 */

const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000003/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

const USER = {
  firstName: 'Анна',
  lastName: 'Иванова',
  email: 'anna@barbersearch.test',
  avatarUrl: null as string | null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved' as PhotoModerationStatus,
}

describe('UserIdentity: аватар', () => {
  it('показывает изображение, когда у пользователя есть фото', async () => {
    stubImageLoading()

    const { container } = render(
      <UserIdentity user={{ ...USER, avatarUrl: AVATAR_URL }} />
    )

    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })
    // Фото декоративно: имя строки — имя и email, а не картинка.
    expect(container.querySelector('[data-slot="avatar-image"]')).toHaveAttribute(
      'alt',
      ''
    )
    expect(screen.getByText('Анна Иванова')).toBeInTheDocument()
    expect(screen.getByText('anna@barbersearch.test')).toBeInTheDocument()
  })

  it('без фото показывает инициалы', () => {
    const { container } = render(<UserIdentity user={USER} />)

    expect(screen.getByText('АИ')).toBeInTheDocument()
    expect(container.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })

  it('при скрытом фото показывает бейдж «Фото скрыто» и сырое изображение', async () => {
    stubImageLoading()

    const { container } = render(
      <UserIdentity
        user={{ ...USER, avatarUrl: AVATAR_URL, isAvatarBlocked: true }}
      />
    )

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })
  })

  it('у кадра «На проверке» показывает бейдж рядом с именем', () => {
    render(<UserIdentity user={{ ...USER, avatarModerationStatus: 'Pending' }} />)

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(screen.queryByText('Фото скрыто')).not.toBeInTheDocument()
  })

  it('у отклонённого кадра показывает «Отклонено»', () => {
    render(
      <UserIdentity user={{ ...USER, avatarModerationStatus: 'Rejected' }} />
    )

    expect(screen.getByText('Отклонено')).toBeInTheDocument()
  })

  it('приоритет бейджей: скрыто важнее отклонения и проверки', () => {
    render(
      <UserIdentity
        user={{
          ...USER,
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
