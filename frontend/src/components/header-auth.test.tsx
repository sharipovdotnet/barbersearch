import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { HeaderAuth } from '@/components/header-auth'
import { ProfileProvider } from '@/components/profile-provider'
import {
  SessionContext,
  type SessionContextValue,
  type SessionStatus,
} from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import type { MyProfile } from '@/lib/my-profile'
import { stubImageLoading } from '@/test/image'
import { server } from '@/test/msw/server'

/**
 * Шапка без сети и без `SessionProvider`: контекст подставляется заглушкой,
 * потому что проверяются только ветки рендера по `status` и состав меню аккаунта (§4.5).
 * `ProfileProvider` настоящий: `HeaderAuth` читает аватар из общего контекста.
 */
const sessionUser: SessionUser = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'session@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
}

const PROFILE: MyProfile = {
  id: sessionUser.id,
  email: sessionUser.email,
  firstName: 'Сергей',
  lastName: 'Сессионов',
  phoneNumber: null,
  avatarUrl: null,
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
  isPhoneVerified: false,
  createdAt: '2026-09-01T10:00:00Z',
  lastLoginAt: null,
}

beforeEach(() => {
  server.use(http.get('/api/users/me', () => HttpResponse.json(PROFILE)))
})

function renderHeader({
  status,
  user = null,
  signOut = vi.fn(async () => {}),
}: {
  status: SessionStatus
  user?: SessionUser | null
  signOut?: () => Promise<void>
}) {
  const context: SessionContextValue = {
    status,
    user,
    signIn: vi.fn(async () => sessionUser),
    signOut,
    retrySession: vi.fn(),
  }

  return render(
    <SessionContext.Provider value={context}>
      <ProfileProvider>
        <HeaderAuth />
      </ProfileProvider>
    </SessionContext.Provider>
  )
}

async function openAccountMenu() {
  const user = userEvent.setup()

  // Base UI открывает меню на mousedown и монтирует всплывающую панель не в том же
  // кадре, поэтому ждём именно её появления. Настоящий клик проверяет Playwright.
  fireEvent.mouseDown(screen.getByRole('button', { name: 'Меню аккаунта' }))
  await screen.findByRole('menu')

  return user
}

describe('HeaderAuth', () => {
  it('в loading показывает статус «Проверяем сессию…» и не показывает «Войти»', () => {
    renderHeader({ status: 'loading' })

    const status = screen.getByRole('status')

    expect(status).toHaveTextContent('Проверяем сессию…')
    expect(screen.queryByRole('button', { name: 'Войти' })).not.toBeInTheDocument()
    // Скелетон декоративный: текст статуса уже объясняет загрузку.
    expect(status.querySelector('[aria-hidden="true"]')).not.toBeNull()
  })

  it('в anonymous показывает «Войти» и не держит статус в DOM', () => {
    renderHeader({ status: 'anonymous' })

    expect(screen.getByRole('button', { name: 'Войти' })).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('в unavailable показывает «Повторить» с внятным именем, а не «Войти»', () => {
    renderHeader({ status: 'unavailable' })

    expect(
      screen.getByRole('button', {
        name: 'Нет связи с сервером. Повторить проверку сессии',
      })
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Войти' })).not.toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('в authenticated держит личные разделы в меню, а не в полосе шапки', () => {
    renderHeader({ status: 'authenticated', user: sessionUser })

    const trigger = screen.getByRole('button', { name: 'Меню аккаунта' })

    expect(trigger).toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
    // Пока меню закрыто, email и ссылки не занимают место в полосе.
    expect(screen.queryByText('session@barbersearch.test')).not.toBeInTheDocument()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('в меню показывает email, «Мой кабинет» и «Выйти»', async () => {
    renderHeader({ status: 'authenticated', user: sessionUser })

    await openAccountMenu()

    expect(screen.getByText('session@barbersearch.test')).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Мой кабинет' })).toHaveAttribute(
      'href',
      '/account'
    )
    expect(screen.getByRole('menuitem', { name: 'Выйти' })).toBeInTheDocument()
    // Клиенту рабочие зоны не показываются.
    expect(screen.queryByRole('menuitem', { name: 'Администрирование' })).not.toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Кабинет барбера' })).not.toBeInTheDocument()
  })

  it('барберу показывает «Кабинет барбера»', async () => {
    renderHeader({
      status: 'authenticated',
      user: { ...sessionUser, role: 'Barber' },
    })

    await openAccountMenu()

    expect(
      screen.getByRole('menuitem', { name: 'Кабинет барбера' })
    ).toHaveAttribute('href', '/barber')
  })

  it('администратору показывает «Администрирование»', async () => {
    renderHeader({
      status: 'authenticated',
      user: { ...sessionUser, role: 'Admin' },
    })

    await openAccountMenu()

    expect(
      screen.getByRole('menuitem', { name: 'Администрирование' })
    ).toHaveAttribute('href', '/admin')
  })

  it('во время выхода пункт недоступен и называется «Выходим…»', async () => {
    let resolveSignOut: () => void = () => {}
    const signOut = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveSignOut = resolve
        })
    )

    renderHeader({ status: 'authenticated', user: sessionUser, signOut })
    const user = await openAccountMenu()

    await user.click(screen.getByRole('menuitem', { name: 'Выйти' }))

    const pending = await screen.findByRole('menuitem', { name: 'Выходим…' })

    // Base UI помечает недоступный пункт `aria-disabled`, а не `disabled`:
    // это div с ролью menuitem, нативного атрибута у него нет.
    expect(pending).toHaveAttribute('aria-disabled', 'true')
    expect(signOut).toHaveBeenCalledOnce()

    resolveSignOut()

    await waitFor(() => {
      expect(screen.getByRole('menuitem', { name: 'Выйти' })).not.toHaveAttribute(
        'aria-disabled',
        'true'
      )
    })
  })
})

describe('HeaderAuth: аватар', () => {
  const avatarUrl =
    '/api/media/avatars/11111111-1111-4111-8111-111111111111/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

  it('показывает изображение профиля в единственной кнопке меню', async () => {
    stubImageLoading()
    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json({ ...PROFILE, avatarUrl })
      )
    )

    renderHeader({ status: 'authenticated', user: sessionUser })

    // Аватар не отдельная кнопка: имя «Меню аккаунта» остаётся одно.
    expect(
      screen.getAllByRole('button', { name: 'Меню аккаунта' })
    ).toHaveLength(1)

    const trigger = screen.getByRole('button', { name: 'Меню аккаунта' })

    await waitFor(() => {
      const image = trigger.querySelector('[data-slot="avatar-image"]')

      expect(image).toHaveAttribute('src', avatarUrl)
      // Изображение декоративно, аватар скрыт от скринридера.
      expect(image).toHaveAttribute('alt', '')
    })

    expect(trigger.querySelector('button')).toBeNull()
    expect(screen.queryByText('СС')).not.toBeInTheDocument()
  })

  it('без avatarUrl показывает инициалы профиля', async () => {
    renderHeader({ status: 'authenticated', user: sessionUser })

    await waitFor(() => {
      // Инициалы рисуются `::before` из `data-initials`: текстового узла внутри
      // кнопки нет, иначе axe ругается на видимый текст вне имени «Меню аккаунта».
      expect(
        document.querySelector('[data-slot="avatar-fallback"]')
      ).toHaveAttribute('data-initials', 'СС')
    })

    expect(document.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })

  it('до загрузки профиля показывает первую букву email', async () => {
    // Ответ не приходит до конца теста: важен первый кадр, а не профиль.
    server.use(
      http.get('/api/users/me', () => new Promise<Response>(() => {}))
    )

    renderHeader({ status: 'authenticated', user: sessionUser })

    const trigger = screen.getByRole('button', { name: 'Меню аккаунта' })

    expect(
      trigger.querySelector('[data-slot="avatar-fallback"]')
    ).toHaveAttribute('data-initials', 'S')
    expect(trigger.querySelector('[data-slot="avatar-image"]')).toBeNull()
  })

  it('с аватаром состав меню и email не меняются', async () => {
    stubImageLoading()
    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json({ ...PROFILE, avatarUrl })
      )
    )

    renderHeader({ status: 'authenticated', user: sessionUser })

    await waitFor(() => {
      expect(
        document.querySelector('[data-slot="avatar-image"]')
      ).not.toBeNull()
    })

    await openAccountMenu()

    expect(screen.getByText('session@barbersearch.test')).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Мой кабинет' })).toHaveAttribute(
      'href',
      '/account'
    )
    expect(screen.getByRole('menuitem', { name: 'Выйти' })).toBeInTheDocument()
  })
})
