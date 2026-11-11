import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { EmailCard } from '@/components/account/email-card'
import type { MyProfile } from '@/lib/my-profile'
import { problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

/**
 * Карточка «Email»: клиентская проверка, привязка кодов к полям и форме,
 * локальный кулдаун после успеха и возврат фокуса на кнопку.
 */

const PROFILE: MyProfile = {
  id: '9e000000-0000-4000-8000-000000000003',
  email: 'old@barbersearch.test',
  firstName: 'Мария',
  lastName: 'Кузнецова',
  phoneNumber: null,
  avatarUrl: null,
  isAvatarBlocked: false,
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:02:00+00:00',
  lastLoginAt: '2026-11-02T14:00:00+00:00',
}

const CHANGE_URL = '/api/auth/email-change'

function renderCard(isEmailVerified = true) {
  render(<EmailCard profile={{ ...PROFILE, isEmailVerified }} />)
}

async function openDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Изменить email' }))
}

async function fillForm(
  user: ReturnType<typeof userEvent.setup>,
  email = 'new@barbersearch.test',
  password = 'current-secret'
) {
  await user.type(screen.getByLabelText('Новый email'), email)
  await user.type(screen.getByLabelText('Текущий пароль'), password)
}

async function submit(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Отправить письмо' }))
}

function errorResponse(status: number, code: string) {
  return HttpResponse.json(
    problemDetails(status, { errorCodes: [code] }),
    { status }
  )
}

describe('EmailCard: карточка', () => {
  it('показывает текущий адрес и статус подтверждения', () => {
    renderCard(false)

    expect(screen.getByText(PROFILE.email)).toBeVisible()
    expect(screen.getByText('Адрес не подтверждён.')).toBeVisible()
    expect(
      screen.getByRole('button', { name: 'Изменить email' })
    ).toBeInTheDocument()
  })
})

describe('EmailCard: клиентские проверки', () => {
  it('пустая форма показывает оба обязательных поля без POST', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CHANGE_URL)

    renderCard()
    await openDialog(user)
    await submit(user)

    expect(screen.getByText('Введите email.')).toBeVisible()
    expect(screen.getByText('Введите текущий пароль.')).toBeVisible()
    expect(calls()).toBe(0)
  })

  it('кривой адрес не отправляется', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CHANGE_URL)

    renderCard()
    await openDialog(user)
    await fillForm(user, 'not-an-email')
    await submit(user)

    expect(screen.getByText('Похоже, в адресе опечатка.')).toBeVisible()
    expect(calls()).toBe(0)
  })
})

describe('EmailCard: серверные ошибки', () => {
  it('занятый адрес показывается у поля «Новый email»', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CHANGE_URL)

    server.use(
      http.post(CHANGE_URL, () =>
        errorResponse(409, 'user.changeEmail.emailTaken')
      )
    )

    renderCard()
    await openDialog(user)
    await fillForm(user)
    await submit(user)

    expect(
      await screen.findByText('Этот адрес уже занят. Укажите другой.')
    ).toBeVisible()
    expect(screen.getByLabelText('Новый email')).toHaveAttribute(
      'aria-invalid',
      'true'
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(calls()).toBe(1)
  })

  it('неверный текущий пароль показывается у поля пароля', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(CHANGE_URL, () =>
        errorResponse(400, 'auth.emailChange.invalidCurrentPassword')
      )
    )

    renderCard()
    await openDialog(user)
    await fillForm(user)
    await submit(user)

    expect(
      await screen.findByText('Текущий пароль неверный.')
    ).toBeVisible()
    expect(screen.getByLabelText('Текущий пароль')).toHaveAttribute(
      'aria-invalid',
      'true'
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it.each([
    ['auth.emailChange.cooldown', 'Письмо уже отправлено. Повторите через минуту.'],
    ['auth.emailChange.unavailable', 'Отправка писем пока не настроена.'],
  ])('код %s остаётся ошибкой формы', async (code, message) => {
    const user = userEvent.setup()

    server.use(http.post(CHANGE_URL, () => errorResponse(409, code)))

    renderCard()
    await openDialog(user)
    await fillForm(user)
    await submit(user)

    expect(await screen.findByRole('alert')).toHaveTextContent(message)
    expect(screen.getByLabelText('Новый email')).not.toHaveAttribute(
      'aria-invalid',
      'true'
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('код sameEmail не отправляет адрес повторно', async () => {
    const user = userEvent.setup()

    server.use(
      http.post(CHANGE_URL, () =>
        errorResponse(400, 'user.changeEmail.sameEmail')
      )
    )

    renderCard()
    await openDialog(user)
    await fillForm(user, PROFILE.email)
    await submit(user)

    expect(
      await screen.findByText('Этот адрес уже указан в профиле.')
    ).toBeVisible()
  })
})

describe('EmailCard: успех', () => {
  it('объявляет отправку, закрывает диалог, включает кулдаун и возвращает фокус', async () => {
    const user = userEvent.setup()
    const calls = countRequests(CHANGE_URL)
    let payload: unknown = null

    server.use(
      http.post(CHANGE_URL, async ({ request }) => {
        payload = await request.json()

        return new HttpResponse(null, { status: 204 })
      })
    )

    renderCard()
    await openDialog(user)
    await fillForm(user)
    await submit(user)

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Письмо отправлено на new@barbersearch.test'
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(calls()).toBe(1)
    expect(payload).toEqual({
      newEmail: 'new@barbersearch.test',
      currentPassword: 'current-secret',
    })

    const cooldown = await screen.findByRole('button', {
      name: /Повторить через \d+ с/,
    })

    expect(cooldown).toHaveAttribute('aria-disabled', 'true')
    expect(cooldown).toHaveFocus()
    // Повторный клик во время кулдауна не открывает диалог и не шлёт запрос.
    await user.click(cooldown)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(calls()).toBe(1)
  })

  it('Escape закрывает диалог и возвращает фокус на кнопку', async () => {
    const user = userEvent.setup()

    renderCard()
    await openDialog(user)

    expect(screen.getByRole('dialog')).toBeInTheDocument()

    await user.keyboard('{Escape}')

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })
    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: 'Изменить email' })
      ).toHaveFocus()
    })
  })
})
