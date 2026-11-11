import { type ComponentProps, type ReactElement } from 'react'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { UserDetail } from '@/components/admin/user-detail'
import type { AdminUserDetail } from '@/lib/admin-users'

/**
 * Карточка пользователя: запись согласия (или её отсутствие), а также модерация
 * аватара — бейдж, кнопки, подтверждение и отсутствие фото.
 *
 * Проверяется только чтение и действия: IP в ответе нет, редакция и время
 * показываются в UTC.
 */
const BASE_USER: AdminUserDetail = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'anna@example.com',
  firstName: 'Анна',
  lastName: 'Иванова',
  phoneNumber: null,
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  avatarRejectionReason: null,
  role: 'Customer',
  isActive: true,
  isEmailVerified: false,
  isPhoneVerified: false,
  barberProfileId: null,
  barberProfile: null,
  lastLoginAt: null,
  lockoutEnd: null,
  failedLoginAttempts: 0,
  consentPolicyVersion: null,
  consentedAt: null,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
}

const AVATAR_URL =
  '/api/media/avatars/11111111-1111-4111-8111-111111111111/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

type DetailProps = ComponentProps<typeof UserDetail>

function detailTree(
  user: AdminUserDetail,
  overrides: Partial<DetailProps> = {}
): ReactElement {
  return (
    <UserDetail
      user={user}
      isSelf={false}
      pendingAction={null}
      actionError={null}
      onChangeRole={() => {}}
      onBlock={() => {}}
      onUnblock={() => {}}
      onVerifyEmail={() => {}}
      onVerifyPhone={() => {}}
      onBlockAvatar={() => {}}
      onUnblockAvatar={() => {}}
      onBack={() => {}}
      {...overrides}
    />
  )
}

function renderDetail(
  user: AdminUserDetail,
  overrides: Partial<DetailProps> = {}
) {
  return render(detailTree(user, overrides))
}

describe('UserDetail, согласие на обработку персональных данных', () => {
  it('показывает редакцию Политики и время согласия в UTC', () => {
    renderDetail({
      ...BASE_USER,
      consentPolicyVersion: '2026-11-03',
      consentedAt: '2026-11-03T14:05:00Z',
    })

    expect(
      screen.getByText('Редакция 2026-11-03 · 03.11.2026, 14:05 UTC')
    ).toBeInTheDocument()
  })

  it('без записи согласия говорит «Записи нет», а не «Согласие не получено»', () => {
    renderDetail(BASE_USER)

    expect(
      screen.getByText('Записи нет — аккаунт создан до 11.11.2026')
    ).toBeInTheDocument()
    expect(screen.queryByText(/Согласие не получено/)).not.toBeInTheDocument()
  })

  it('не добавляет действий над согласием: только чтение', () => {
    renderDetail({
      ...BASE_USER,
      consentPolicyVersion: '2026-11-03',
      consentedAt: '2026-11-03T14:05:00Z',
    })

    expect(screen.queryByRole('button', { name: /соглас/i })).not.toBeInTheDocument()
  })
})

describe('UserDetail, модерация аватара', () => {
  it('без фото показывает подсказку и не предлагает действий', () => {
    renderDetail(BASE_USER)

    expect(
      screen.getByText('У пользователя нет фото — скрывать нечего.')
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть фото' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Вернуть фото' })
    ).not.toBeInTheDocument()
  })

  it('скрывает фото только после подтверждения в alertdialog', async () => {
    const user = userEvent.setup()
    const onBlockAvatar = vi.fn()

    renderDetail(
      { ...BASE_USER, avatarUrl: AVATAR_URL },
      { onBlockAvatar }
    )

    await user.click(screen.getByRole('button', { name: 'Скрыть фото' }))

    const dialog = screen.getByRole('alertdialog')

    expect(dialog).toHaveTextContent('Скрыть фото?')
    expect(dialog).toHaveTextContent(
      'Фото исчезнет из шапки, поиска, карточек и списков броней. Аккаунт останется активным, вернуть фото можно здесь же.'
    )
    expect(onBlockAvatar).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole('button', { name: 'Скрыть фото' }))

    expect(onBlockAvatar).toHaveBeenCalledOnce()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('скрытое фото показывает бейдж и возвращает без диалога', async () => {
    const user = userEvent.setup()
    const onUnblockAvatar = vi.fn()

    renderDetail(
      { ...BASE_USER, avatarUrl: AVATAR_URL, isAvatarBlocked: true },
      { onUnblockAvatar }
    )

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть фото' })
    ).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Вернуть фото' }))

    expect(onUnblockAvatar).toHaveBeenCalledOnce()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('во время скрытия показывает loader и блокирует кнопку', () => {
    renderDetail(
      { ...BASE_USER, avatarUrl: AVATAR_URL },
      { pendingAction: 'blockAvatar' }
    )

    const button = screen.getByRole('button', { name: 'Скрываем…' })

    expect(button).toBeDisabled()
  })

  it('кадр «На проверке» показывает решение вместо блокировки', () => {
    renderDetail({
      ...BASE_USER,
      avatarUrl: AVATAR_URL,
      avatarModerationStatus: 'Pending',
    })

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Одобрить' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Отклонить' })).toBeEnabled()
    expect(
      screen.queryByRole('button', { name: 'Скрыть фото' })
    ).not.toBeInTheDocument()
  })

  it('отклонённый кадр показывает причину и не предлагает решения', () => {
    renderDetail({
      ...BASE_USER,
      avatarUrl: AVATAR_URL,
      avatarModerationStatus: 'Rejected',
      avatarRejectionReason: 'Кадр размыт.',
    })

    expect(screen.getByText('Отклонено')).toBeInTheDocument()
    expect(screen.getByText('Причина: Кадр размыт.')).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Одобрить' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отклонить' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть фото' })
    ).not.toBeInTheDocument()
  })

  it('скрытый кадр приоритетнее статуса проверки', () => {
    renderDetail({
      ...BASE_USER,
      avatarUrl: AVATAR_URL,
      isAvatarBlocked: true,
      avatarModerationStatus: 'Pending',
    })

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
    expect(screen.queryByText('На проверке')).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Вернуть фото' })
    ).toBeInTheDocument()
  })

  it('ошибку действия показывает в role="alert"', () => {
    renderDetail(
      { ...BASE_USER, avatarUrl: AVATAR_URL },
      { actionError: 'Не удалось скрыть фото. Попробуйте ещё раз.' }
    )

    expect(screen.getByRole('alert')).toHaveTextContent(
      'Не удалось скрыть фото. Попробуйте ещё раз.'
    )
  })

  it('после ответа сервера показывает «Вернуть фото» без перезагрузки', () => {
    const view = renderDetail({ ...BASE_USER, avatarUrl: AVATAR_URL })

    expect(
      screen.getByRole('button', { name: 'Скрыть фото' })
    ).toBeInTheDocument()

    view.rerender(
      detailTree({
        ...BASE_USER,
        avatarUrl: AVATAR_URL,
        isAvatarBlocked: true,
      })
    )

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Вернуть фото' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Скрыть фото' })
    ).not.toBeInTheDocument()
  })
})
