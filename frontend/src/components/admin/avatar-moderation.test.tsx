import {
  type ComponentProps,
  type ReactElement,
} from 'react'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { AvatarModeration } from '@/components/admin/avatar-moderation'
import { noContent, problemDetails } from '@/test/msw/handlers'
import { server } from '@/test/msw/server'

const USER_ID = '9e000000-0000-4000-8000-000000000003'
const AVATAR_URL = `/api/media/avatars/${USER_ID}/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg`

type ModerationProps = ComponentProps<typeof AvatarModeration>

function moderationTree(
  overrides: Partial<ModerationProps> = {}
): ReactElement {
  return (
    <AvatarModeration
      avatarUrl={AVATAR_URL}
      isAvatarBlocked={false}
      moderationStatus="Pending"
      rejectionReason={null}
      userId={USER_ID}
      subject="Анна Иванова"
      initials="АИ"
      isBusy={false}
      pendingAction={null}
      onBlock={() => {}}
      onUnblock={() => {}}
      {...overrides}
    >
      <h2>Анна Иванова</h2>
      <p>anna@barbersearch.test</p>
    </AvatarModeration>
  )
}

function renderModeration(overrides: Partial<ModerationProps> = {}) {
  return render(moderationTree(overrides))
}

describe('AvatarModeration: статусы премодерации', () => {
  it('кадр «На проверке» предлагает одобрить или отклонить', () => {
    renderModeration()

    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Одобрить' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Отклонить' })).toBeEnabled()
    expect(
      screen.queryByRole('button', { name: 'Скрыть фото' })
    ).not.toBeInTheDocument()
  })

  it('одобренный кадр оставляет только «Скрыть фото»', () => {
    renderModeration({ moderationStatus: 'Approved' })

    expect(
      screen.getByRole('button', { name: 'Скрыть фото' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Одобрить' })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Отклонить' })
    ).not.toBeInTheDocument()
  })

  it('отклонённый кадр показывает причину без кнопок решения', () => {
    renderModeration({
      moderationStatus: 'Rejected',
      rejectionReason: 'Кадр размыт.',
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

  it('скрытый кадр приоритетнее статуса: только «Вернуть фото»', () => {
    renderModeration({
      isAvatarBlocked: true,
      moderationStatus: 'Pending',
    })

    expect(screen.getByText('Фото скрыто')).toBeInTheDocument()
    expect(screen.queryByText('На проверке')).not.toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Вернуть фото' })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Одобрить' })
    ).not.toBeInTheDocument()
  })
})

describe('AvatarModeration: решения администратора', () => {
  it('«Одобрить» отправляет POST и обновляет строку без второго GET', async () => {
    const user = userEvent.setup()
    let path = ''
    let method = ''

    server.use(
      http.post(
        '/api/admin/users/:userId/avatar/approval',
        ({ request }) => {
          path = new URL(request.url).pathname
          method = request.method

          return noContent()
        }
      )
    )

    renderModeration()

    await user.click(screen.getByRole('button', { name: 'Одобрить' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото одобрено.'
    )
    expect(path).toBe(`/api/admin/users/${USER_ID}/avatar/approval`)
    expect(method).toBe('POST')
    expect(
      screen.getByRole('button', { name: 'Скрыть фото' })
    ).toBeInTheDocument()
    expect(screen.queryByText('На проверке')).not.toBeInTheDocument()
  })

  it('«Отклонить» требует причину, отправляет её и показывает отказ', async () => {
    const user = userEvent.setup()
    let path = ''
    let body: unknown = null

    server.use(
      http.post(
        '/api/admin/users/:userId/avatar/rejection',
        async ({ request }) => {
          path = new URL(request.url).pathname
          body = await request.json()

          return noContent()
        }
      )
    )

    renderModeration()

    await user.click(screen.getByRole('button', { name: 'Отклонить' }))

    const dialog = screen.getByRole('alertdialog')

    // Пустая причина не уходит на сервер: ошибка у поля, диалог открыт.
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(within(dialog).getByRole('alert')).toHaveTextContent(
      'Укажите причину отказа.'
    )
    expect(body).toBeNull()

    await user.type(
      within(dialog).getByLabelText('Причина отказа'),
      'Кадр размыт.'
    )
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Фото отклонено.'
    )
    expect(path).toBe(`/api/admin/users/${USER_ID}/avatar/rejection`)
    expect(body).toStrictEqual({ reason: 'Кадр размыт.' })
    expect(screen.getByText('Отклонено')).toBeInTheDocument()
    expect(screen.getByText('Причина: Кадр размыт.')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('после отмены диалога фокус возвращается на «Отклонить»', async () => {
    const user = userEvent.setup()

    renderModeration()

    const trigger = screen.getByRole('button', { name: 'Отклонить' })

    await user.click(trigger)
    await user.click(
      within(screen.getByRole('alertdialog')).getByRole('button', {
        name: 'Отмена',
      })
    )

    await waitFor(() => {
      expect(trigger).toHaveFocus()
    })
  })

  it('409 говорит, что фото уже проверено, и не выдаёт решение за успех', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/admin/users/:userId/avatar/approval', () =>
        HttpResponse.json(
          problemDetails(409, {
            errorCodes: ['user.avatarModeration.invalidStatus'],
          }),
          { status: 409 }
        )
      )
    )

    renderModeration()

    await user.click(screen.getByRole('button', { name: 'Одобрить' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Фото уже проверено. Обновите список.'
    )
    expect(screen.getByText('На проверке')).toBeInTheDocument()
    expect(
      screen.queryByRole('status')
    ).not.toBeInTheDocument()
  })

  it('ошибку отказа показывает в alertdialog и оставляет диалог открытым', async () => {
    const user = userEvent.setup()

    server.use(
      http.post('/api/admin/users/:userId/avatar/rejection', () =>
        HttpResponse.json(
          problemDetails(400, {
            errorCodes: ['user.avatarModeration.reasonRequired'],
          }),
          { status: 400 }
        )
      )
    )

    renderModeration()

    await user.click(screen.getByRole('button', { name: 'Отклонить' }))

    const dialog = screen.getByRole('alertdialog')

    await user.type(
      within(dialog).getByLabelText('Причина отказа'),
      'Кадр размыт.'
    )
    await user.click(within(dialog).getByRole('button', { name: 'Отклонить' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Укажите причину отказа.'
    )
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
  })
})
