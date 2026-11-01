import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HeaderAuth } from '@/components/header-auth'
import {
  SessionContext,
  type SessionContextValue,
  type SessionStatus,
} from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'

/**
 * Шапка без сети и без `SessionProvider`: контекст подставляется заглушкой,
 * потому что проверяются только ветки рендера по `status` (§4.5).
 */
const sessionUser: SessionUser = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'session@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
}

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
    signIn: vi.fn(async () => {}),
    signOut,
    retrySession: vi.fn(),
  }

  return render(
    <SessionContext.Provider value={context}>
      <HeaderAuth />
    </SessionContext.Provider>
  )
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

  it('в authenticated показывает email, «Мои записи» и «Выйти»', () => {
    renderHeader({ status: 'authenticated', user: sessionUser })

    expect(screen.getByText('session@barbersearch.test')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Мои записи' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Выйти' })).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('во время выхода «Выйти» недоступна и называется «Выходим…»', async () => {
    const user = userEvent.setup()
    let resolveSignOut: () => void = () => {}
    const signOut = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveSignOut = resolve
        })
    )

    renderHeader({ status: 'authenticated', user: sessionUser, signOut })

    await user.click(screen.getByRole('button', { name: 'Выйти' }))

    const pending = screen.getByRole('button', { name: 'Выходим…' })

    expect(pending).toBeDisabled()
    expect(signOut).toHaveBeenCalledOnce()

    resolveSignOut()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Выйти' })).toBeEnabled()
    })
  })
})
