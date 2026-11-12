import { useState } from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AvatarCard } from '@/components/account/avatar-card'
import {
  AccountProfileContext,
  type AccountProfileValue,
} from '@/components/account/account-profile-context'
import { prepareAvatarImage } from '@/lib/image-prep'
import type { MyProfile } from '@/lib/my-profile'
import type { PhotoModerationStatus } from '@/lib/photo-moderation'
import { stubImageLoading } from '@/test/image'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const { invalidateBarberProfileMock } = vi.hoisted(() => ({
  invalidateBarberProfileMock: vi.fn(),
}))

vi.mock('@/lib/catalog', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/catalog')>()

  return {
    ...actual,
    invalidateBarberProfile: invalidateBarberProfileMock,
  }
})

vi.mock('@/lib/image-prep', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/image-prep')>()

  return {
    ...actual,
    // jsdom не рисует canvas: подготовку подменяем, транспорт остаётся настоящим.
    prepareAvatarImage: vi.fn(async (file: File) => file),
  }
})

const BARBER_PROFILE_ID = '9e100000-0000-4000-8000-000000000015'
const AVATAR_URL =
  '/api/media/avatars/9e000000-0000-4000-8000-000000000004/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

const BASE_PROFILE: MyProfile = {
  id: '9e000000-0000-4000-8000-000000000004',
  email: 'mark@barbersearch.test',
  firstName: 'Марк',
  lastName: 'Правкин',
  phoneNumber: null,
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  avatarRejectionReason: null,
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:00:00Z',
  lastLoginAt: null,
}

/**
 * Каркас кабинета не нужен: карточке важны профиль и `setAvatar` из контекста.
 *
 * `setAvatar` повторяет провайдер: новая загрузка или удаление снимают флаг
 * модерации, потому что блокировка относилась к прежнему кадру.
 */
function Harness({
  initialAvatarUrl = null,
  initialBlocked = false,
  initialStatus = 'Approved',
  initialReason = null,
  barberProfileId = null,
}: {
  initialAvatarUrl?: string | null
  initialBlocked?: boolean
  initialStatus?: PhotoModerationStatus
  initialReason?: string | null
  barberProfileId?: string | null
}) {
  const [avatar, setAvatar] = useState<{
    avatarUrl: string | null
    isAvatarBlocked: boolean
    moderationStatus: PhotoModerationStatus
    rejectionReason: string | null
  }>({
    avatarUrl: initialAvatarUrl,
    isAvatarBlocked: initialBlocked,
    moderationStatus: initialStatus,
    rejectionReason: initialReason,
  })
  const profile: MyProfile = {
    ...BASE_PROFILE,
    avatarUrl: avatar.avatarUrl,
    isAvatarBlocked: avatar.isAvatarBlocked,
    avatarModerationStatus: avatar.moderationStatus,
    avatarRejectionReason: avatar.rejectionReason,
    barberProfileId,
  }
  const value: AccountProfileValue = {
    status: 'ready',
    profile,
    error: null,
    requiresSignIn: false,
    reload: async () => {},
    save: async () => profile,
    setAvatar: (avatarUrl: string | null, status: PhotoModerationStatus) =>
      setAvatar({
        avatarUrl,
        isAvatarBlocked: false,
        moderationStatus: status,
        rejectionReason: null,
      }),
  }

  return (
    <AccountProfileContext.Provider value={value}>
      <AvatarCard profile={profile} />
    </AccountProfileContext.Provider>
  )
}

function avatarFile(name = 'avatar.png'): File {
  return new File(['avatar-bytes'], name, { type: 'image/png' })
}

beforeEach(() => {
  invalidateBarberProfileMock.mockClear()
  vi.mocked(prepareAvatarImage).mockClear()
})

describe('AvatarCard: загрузка', () => {
  it('загружает файл, объявляет проверку и показывает бейдж «На проверке»', async () => {
    const user = userEvent.setup()
    const posts = countRequests('/api/users/me/avatar')

    server.use(
      http.post('/api/users/me/avatar', () =>
        HttpResponse.json(
          { avatarUrl: AVATAR_URL, moderationStatus: 'Pending' },
          { status: 201 }
        )
      )
    )

    render(<Harness barberProfileId={BARBER_PROFILE_ID} />)

    expect(
      screen.getByRole('button', { name: 'Загрузить фото' })
    ).toBeInTheDocument()

    await user.upload(screen.getByLabelText('Файл'), avatarFile())

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Аватар загружен и ждёт проверки.'
    )
    expect(posts()).toBe(1)
    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Кадр виден только вам, пока администратор его не одобрит.'
      )
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Изменить фото' })
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Удалить' })).toBeInTheDocument()
    // Публичная карточка барбера не должна показывать старый кэш.
    expect(invalidateBarberProfileMock).toHaveBeenCalledWith(BARBER_PROFILE_ID)
  })

  it('отказ типа показывается на карточке и не доходит до подготовки и сети', async () => {
    // `accept` — подсказка браузеру, а проверка типа обязана сработать и без неё.
    const user = userEvent.setup({ applyAccept: false })
    const posts = countRequests('/api/users/me/avatar')

    render(<Harness />)

    await user.upload(
      screen.getByLabelText('Файл'),
      new File(['plain'], 'notes.txt', { type: 'text/plain' })
    )

    expect(
      await screen.findByRole('alert')
    ).toHaveTextContent('Поддерживаются только JPEG, PNG и WebP.')
    expect(prepareAvatarImage).not.toHaveBeenCalled()
    expect(posts()).toBe(0)
  })

  it('413 без ProblemDetails — ошибка файла', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/users/me/avatar', () =>
        new HttpResponse('Payload Too Large', { status: 413 })
      )
    )

    render(<Harness />)

    await user.upload(screen.getByLabelText('Файл'), avatarFile())

    expect(
      await screen.findByRole('alert')
    ).toHaveTextContent('Файл слишком большой. Максимум — 5 МБ.')
  })

  it('401 объясняет истёкшую сессию, а не английский detail', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/users/me/avatar', () =>
        HttpResponse.json(
          problemDetails(401, {
            detail: 'Authentication is required.',
            errorCodes: ['auth.required'],
          }),
          { status: 401 }
        )
      )
    )

    render(<Harness />)

    await user.upload(screen.getByLabelText('Файл'), avatarFile())

    const alert = await screen.findByRole('alert')

    expect(alert).toHaveTextContent('Сессия истекла — войдите снова.')
    expect(alert).not.toHaveTextContent('Authentication is required.')
  })
})

describe('AvatarCard: удаление', () => {
  it('спрашивает alertdialog, удаляет аватар, возвращает инициалы и фокус', async () => {
    const user = userEvent.setup()
    const deletes = countRequests('/api/users/me/avatar')

    server.use(
      http.delete('/api/users/me/avatar', () =>
        new HttpResponse(null, { status: 204 })
      )
    )

    render(
      <Harness
        initialAvatarUrl={AVATAR_URL}
        barberProfileId={BARBER_PROFILE_ID}
      />
    )

    await user.click(screen.getByRole('button', { name: 'Удалить' }))

    const dialog = screen.getByRole('alertdialog')

    expect(dialog).toHaveTextContent('Удалить аватар?')
    expect(dialog).toHaveTextContent(
      'Аватар исчезнет из карточки и кабинета. Файл удаляется безвозвратно.'
    )

    await user.click(
      within(dialog).getByRole('button', { name: /^Удалить$/ })
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Аватар удалён.'
    )
    expect(deletes()).toBe(1)
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(screen.getByText('МП')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Загрузить фото' })
    ).toBeInTheDocument()
    expect(invalidateBarberProfileMock).toHaveBeenCalledWith(BARBER_PROFILE_ID)

    // Кнопки «Удалить» больше нет: фокус обязан вернуться на загрузку.
    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Загрузить фото' })
      ).toHaveFocus()
    })
  })

  it('ошибка удаления остаётся в alertdialog и не закрывает его', async () => {
    const user = userEvent.setup()

    server.use(
      http.delete('/api/users/me/avatar', () =>
        HttpResponse.json(
          problemDetails(500, { errorCodes: ['user.uploadAvatar.storage'] }),
          { status: 500 }
        )
      )
    )

    render(<Harness initialAvatarUrl={AVATAR_URL} />)

    await user.click(screen.getByRole('button', { name: 'Удалить' }))

    const dialog = screen.getByRole('alertdialog')

    await user.click(
      within(dialog).getByRole('button', { name: /^Удалить$/ })
    )

    expect(
      await within(dialog).findByRole('alert')
    ).toHaveTextContent('Не удалось сохранить файл. Попробуйте ещё раз.')
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
  })
})

describe('AvatarCard: фото скрыто администратором', () => {
  it('показывает инициалы и объяснение вместо изображения', () => {
    const { container } = render(
      <Harness initialAvatarUrl={AVATAR_URL} initialBlocked />
    )

    // Сырой URL у профиля остаётся, но кадр не рендерится: браузер показал бы
    // его из 24-часового кэша даже после 404.
    expect(container.querySelector('[data-slot="avatar-image"]')).toBeNull()
    expect(screen.getByText('МП')).toBeInTheDocument()
    expect(
      screen.getByText('Фото скрыто администратором')
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Его не видно публично. Новое фото появится публично после проверки, а вернуть старое может только администратор.'
      )
    ).toBeInTheDocument()
    // Загрузка доступна и называется иначе: это новый кадр, а не правка старого.
    expect(
      screen.getByRole('button', { name: 'Загрузить другое фото' })
    ).toBeInTheDocument()
    // Удаление остаётся: оно снимает и URL, и флаг.
    expect(screen.getByRole('button', { name: 'Удалить' })).toBeInTheDocument()
  })

  it('новая загрузка снимает флаг и показывает фото', async () => {
    const user = userEvent.setup()
    stubImageLoading()

    server.use(
      http.post('/api/users/me/avatar', () =>
        HttpResponse.json(
          { avatarUrl: AVATAR_URL, moderationStatus: 'Pending' },
          { status: 201 }
        )
      )
    )

    const { container } = render(
      <Harness initialAvatarUrl={AVATAR_URL} initialBlocked />
    )

    await user.upload(screen.getByLabelText('Файл'), avatarFile())

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Аватар загружен и ждёт проверки.'
    )
    expect(
      screen.queryByText('Фото скрыто администратором')
    ).not.toBeInTheDocument()
    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Изменить фото' })
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute(
        'srcset',
        `${AVATAR_URL}?size=sm 1x, ${AVATAR_URL}?size=md 2x, ${AVATAR_URL}?size=lg 3x`
      )
    })
  })

  it('удаление снимает флаг и возвращает кнопку «Загрузить фото»', async () => {
    const user = userEvent.setup()

    server.use(
      http.delete('/api/users/me/avatar', () =>
        new HttpResponse(null, { status: 204 })
      )
    )

    const { container } = render(
      <Harness initialAvatarUrl={AVATAR_URL} initialBlocked />
    )

    await user.click(screen.getByRole('button', { name: 'Удалить' }))
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: /^Удалить$/,
      })
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Аватар удалён.'
    )
    expect(
      screen.queryByText('Фото скрыто администратором')
    ).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Загрузить фото' })
    ).toBeInTheDocument()
    expect(container.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })
})

describe('AvatarCard: премодерация', () => {
  it('кадр «На проверке» рендерится с бейджем и объяснением', async () => {
    stubImageLoading()

    const { container } = render(
      <Harness initialAvatarUrl={AVATAR_URL} initialStatus="Pending" />
    )

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.getByText(
        'Кадр виден только вам, пока администратор его не одобрит.'
      )
    ).toBeInTheDocument()
    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute(
        'srcset',
        `${AVATAR_URL}?size=sm 1x, ${AVATAR_URL}?size=md 2x, ${AVATAR_URL}?size=lg 3x`
      )
    })
    // Удаление доступно и до решения администратора.
    expect(screen.getByRole('button', { name: 'Удалить' })).toBeInTheDocument()
  })

  it('отклонённый кадр показывает причину и «Загрузить другое фото»', async () => {
    stubImageLoading()

    const { container } = render(
      <Harness
        initialAvatarUrl={AVATAR_URL}
        initialStatus="Rejected"
        initialReason="Кадр размыт."
      />
    )

    expect(screen.getByText('Отклонено')).toBeInTheDocument()
    expect(screen.getByText('Причина: Кадр размыт.')).toBeInTheDocument()
    // Отклонённый кадр в кабинете виден: владелец должен понимать, что именно
    // не прошло проверку.
    await waitFor(() => {
      expect(
        container.querySelector('[data-slot="avatar-image"]')
      ).toHaveAttribute('src', AVATAR_URL)
    })
    expect(
      screen.getByRole('button', { name: 'Загрузить другое фото' })
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Удалить' })).toBeInTheDocument()
  })

  it('удаление отклонённого кадра возвращает фокус на загрузку', async () => {
    const user = userEvent.setup()

    server.use(
      http.delete('/api/users/me/avatar', () =>
        new HttpResponse(null, { status: 204 })
      )
    )

    render(
      <Harness
        initialAvatarUrl={AVATAR_URL}
        initialStatus="Rejected"
        initialReason="Кадр размыт."
      />
    )

    await user.click(screen.getByRole('button', { name: 'Удалить' }))
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: /^Удалить$/,
      })
    )

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Аватар удалён.'
    )
    expect(screen.queryByText('Отклонено')).not.toBeInTheDocument()

    // Кнопки «Удалить» больше нет: фокус обязан вернуться на загрузку.
    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Загрузить фото' })
      ).toHaveFocus()
    })
  })
})
