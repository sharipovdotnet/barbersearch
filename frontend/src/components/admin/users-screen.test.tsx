import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { UsersScreen } from '@/components/admin/users-screen'
import { AUTO_REFRESH_INTERVAL_MS } from '@/hooks/use-auto-refresh'
import {
  SessionContext,
  type SessionContextValue,
} from '@/hooks/use-session'
import type { AdminUserDetail, AdminUserRow } from '@/lib/admin-users'
import type { SessionUser } from '@/lib/auth'
import { server } from '@/test/msw/server'

/**
 * Автообновление экрана «Пользователи».
 *
 * Список должен сам подтягивать новые аккаунты раз в период опроса, но тик — это
 * тихое обновление: выбранная карточка не теряется, открытый диалог не закрывается,
 * а скрытая вкладка не тратит запросы. Сеть — только MSW на границе HTTP; запросы
 * считаются по URL, потому что число запросов и есть проверяемое поведение.
 */

const USER_ID = '9e000000-0000-4000-8000-000000000101'

/**
 * Строка списка и карточка — один и тот же аккаунт.
 *
 * Фикстура минимальна, но проходит строгие `isAdminUserPage`/`isAdminUser`:
 * битое поле превратило бы экран в его error-состояние, а не в список.
 */
const ROW: AdminUserRow = {
  id: USER_ID,
  email: 'anna@barbersearch.test',
  firstName: 'Анна',
  lastName: 'Иванова',
  avatarUrl: null,
  isAvatarBlocked: false,
  avatarModerationStatus: 'Approved',
  role: 'Customer',
  isActive: true,
  isEmailVerified: true,
  isPhoneVerified: false,
  barberProfileId: null,
  lastLoginAt: null,
  createdAt: '2026-10-01T10:00:00+00:00',
}

const DETAIL: AdminUserDetail = {
  ...ROW,
  phoneNumber: null,
  avatarRejectionReason: null,
  barberProfile: null,
  lockoutEnd: null,
  failedLoginAttempts: 0,
  consentPolicyVersion: null,
  consentedAt: null,
  updatedAt: '2026-10-01T10:00:00+00:00',
}

const ADMIN: SessionUser = {
  id: '55555555-5555-4555-8555-555555555555',
  email: 'admin@barbersearch.test',
  role: 'Admin',
  barberProfileId: null,
  isEmailVerified: true,
}

const session: SessionContextValue = {
  status: 'authenticated',
  user: ADMIN,
  signIn: async () => ADMIN,
  signOut: async () => {},
  retrySession: () => {},
}

/** Запросы списка и карточки по отдельности: тик трогает только список. */
type RecordedRequests = {
  list: string[]
  detail: string[]
}

function mockUsers(): RecordedRequests {
  const requests: RecordedRequests = { list: [], detail: [] }

  server.use(
    http.get('/api/admin/users', ({ request }) => {
      requests.list.push(request.url)

      return HttpResponse.json({ items: [ROW], nextCursor: null })
    }),
    http.get('/api/admin/users/:id', ({ request }) => {
      requests.detail.push(request.url)

      return HttpResponse.json(DETAIL)
    })
  )

  return requests
}

/**
 * `matchMedia` отвечает «широкий экран».
 *
 * На телефоне выбранная карточка заменяет список, и шапки с кнопкой «Создать
 * пользователя» в DOM нет. Десктопный split-view — тот же экран, но с обоими
 * действиями на месте, поэтому кнопка и диалог доступны тесту.
 */
function stubWideScreen(): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: true,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }))
}

function renderScreen() {
  return render(
    <SessionContext.Provider value={session}>
      <UsersScreen userId={USER_ID} />
    </SessionContext.Provider>
  )
}

/** Прокручивает время опроса и даёт MSW завершить начатые запросы. */
async function tick(ms = AUTO_REFRESH_INTERVAL_MS): Promise<void> {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

/** Скрытость вкладки: jsdom по умолчанию считает документ видимым. */
function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    get: () => state,
  })
  document.dispatchEvent(new Event('visibilitychange'))
}

beforeEach(() => {
  window.history.replaceState(null, '', `/admin/users/${USER_ID}`)
  stubWideScreen()
})

afterEach(() => {
  vi.useRealTimers()
  // Удаляем собственный геттер — возвращается настоящий из прототипа jsdom.
  Reflect.deleteProperty(document, 'visibilityState')
})

describe('UsersScreen: автообновление списка', () => {
  it('через период опроса сам перечитывает список', async () => {
    vi.useFakeTimers()
    const requests = mockUsers()

    renderScreen()
    await tick(0)

    // Монтирование — ровно один запрос: таймер не дублирует первую загрузку.
    expect(requests.list).toHaveLength(1)

    await tick(AUTO_REFRESH_INTERVAL_MS - 1)
    expect(requests.list).toHaveLength(1)

    await tick(1)
    expect(requests.list).toHaveLength(2)
  })

  it('тик не сбрасывает выбранную карточку', async () => {
    vi.useFakeTimers()
    const requests = mockUsers()

    renderScreen()
    await tick(0)

    expect(
      screen.getByRole('heading', { name: 'Анна Иванова', level: 2 })
    ).toBeInTheDocument()

    await tick()

    // Карточка осталась на месте, а адрес по-прежнему указывает на выбранный аккаунт.
    expect(
      screen.getByRole('heading', { name: 'Анна Иванова', level: 2 })
    ).toBeInTheDocument()
    expect(window.location.pathname).toBe(`/admin/users/${USER_ID}`)
    // Тик перечитывает только список: карточка продолжает жить своим запросом.
    expect(requests.detail).toHaveLength(1)
  })

  it('тик не мешает открытому диалогу', async () => {
    vi.useFakeTimers()
    const requests = mockUsers()

    renderScreen()
    await tick(0)

    // Клик синхронный: fake timers ломают пошаговый `userEvent`, а Base UI
    // открывает диалог на обычном `click`.
    fireEvent.click(
      screen.getByRole('button', { name: 'Создать пользователя' })
    )

    expect(
      screen.getByRole('heading', { name: 'Новый пользователь' })
    ).toBeInTheDocument()

    await tick()

    // Список обновился под диалогом, но форма осталась открытой.
    expect(requests.list).toHaveLength(2)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(
      within(screen.getByRole('dialog')).getByRole('heading', {
        name: 'Новый пользователь',
      })
    ).toBeInTheDocument()
  })

  it('на скрытой вкладке не опрашивает', async () => {
    vi.useFakeTimers()
    const requests = mockUsers()

    renderScreen()
    await tick(0)
    expect(requests.list).toHaveLength(1)

    act(() => {
      setVisibility('hidden')
    })

    // Три периода подряд: тик молчит, пока вкладка не видна.
    await tick(AUTO_REFRESH_INTERVAL_MS * 3)

    expect(requests.list).toHaveLength(1)
  })
})
