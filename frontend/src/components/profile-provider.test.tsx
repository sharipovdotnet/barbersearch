import { StrictMode, useMemo, useState } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { ProfileProvider } from '@/components/profile-provider'
import { useMyProfile } from '@/hooks/use-my-profile'
import {
  SessionContext,
  type SessionContextValue,
  type SessionStatus,
} from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import type { MyProfile } from '@/lib/my-profile'
import { noContent, problemDetails } from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const USER: SessionUser = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'mark@barbersearch.test',
  role: 'Customer',
  barberProfileId: null,
  isEmailVerified: true,
}

const OTHER_USER: SessionUser = {
  ...USER,
  id: '22222222-2222-4222-8222-222222222222',
  email: 'marina@barbersearch.test',
}

const AVATAR_URL =
  '/api/media/avatars/11111111-1111-4111-8111-111111111111/0192f0c4e5b67c8d9a0b1c2d3e4f5a6b.jpg'

function profile(
  id: string,
  notice = 'Марк',
  avatar: { avatarUrl?: string | null; isAvatarBlocked?: boolean } = {}
): MyProfile {
  return {
    id,
    email: 'mark@barbersearch.test',
    firstName: notice,
    lastName: 'Правкин',
    phoneNumber: null,
    avatarUrl: avatar.avatarUrl ?? null,
    isAvatarBlocked: avatar.isAvatarBlocked ?? false,
    role: 'Customer',
    barberProfileId: null,
    isEmailVerified: true,
    isPhoneVerified: false,
    createdAt: '2026-09-01T10:00:00Z',
    lastLoginAt: null,
  }
}

/** Два независимых подписчика: шапка и кабинет читают один контекст. */
function ProfileProbe({ where }: { where: string }) {
  const { status, profile, error, requiresSignIn } = useMyProfile()

  return (
    <div>
      <output aria-label={`${where}: статус`}>{status}</output>
      <output aria-label={`${where}: профиль`}>
        {profile ? `${profile.firstName} ${profile.lastName}` : 'нет'}
      </output>
      <output aria-label={`${where}: аватар`}>
        {profile?.avatarUrl ?? 'нет'}
      </output>
      <output aria-label={`${where}: блокировка`}>
        {profile?.isAvatarBlocked ? 'да' : 'нет'}
      </output>
      <output aria-label={`${where}: ошибка`}>{error ?? 'нет'}</output>
      <output aria-label={`${where}: вход`}>
        {requiresSignIn ? 'нужен' : 'не нужен'}
      </output>
    </div>
  )
}

/** Действия провайдера дергаются кнопками, чтобы тест не зависел от рендера формы. */
function ProfileActions() {
  const { profile, save, setAvatar, reload } = useMyProfile()

  return (
    <div>
      <button type="button" onClick={() => setAvatar(AVATAR_URL)}>
        Новый аватар
      </button>
      <button type="button" onClick={() => setAvatar(null)}>
        Убрать аватар
      </button>
      <button
        type="button"
        onClick={() => {
          void save({
            firstName: ' Марк ',
            lastName: 'Правкин',
            phoneNumber: null,
            avatarUrl: profile?.avatarUrl ?? null,
          })
        }}
      >
        Сохранить профиль
      </button>
      <button
        type="button"
        onClick={() => {
          void reload()
        }}
      >
        Обновить профиль
      </button>
    </div>
  )
}

function Harness({
  initialStatus = 'authenticated',
  initialUser = USER,
}: {
  initialStatus?: SessionStatus
  initialUser?: SessionUser | null
}) {
  const [session, setSession] = useState<{
    status: SessionStatus
    user: SessionUser | null
  }>({ status: initialStatus, user: initialUser })

  const value = useMemo<SessionContextValue>(
    () => ({
      ...session,
      signIn: async () => initialUser ?? USER,
      signOut: async () => {
        setSession({ status: 'anonymous', user: null })
      },
      retrySession: () => {},
    }),
    [initialUser, session]
  )

  return (
    <SessionContext.Provider value={value}>
      <ProfileProvider>
        <ProfileProbe where="шапка" />
        <ProfileProbe where="кабинет" />
        <ProfileActions />
        <button
          type="button"
          onClick={() => {
            setSession({ status: 'anonymous', user: null })
          }}
        >
          Выйти
        </button>
        <button
          type="button"
          onClick={() => {
            setSession({ status: 'authenticated', user: OTHER_USER })
          }}
        >
          Сменить аккаунт
        </button>
        <button
          type="button"
          onClick={() => {
            setSession((current) => ({ ...current }))
          }}
        >
          Лишний рендер
        </button>
      </ProfileProvider>
    </SessionContext.Provider>
  )
}

function renderHarness(options: Parameters<typeof Harness>[0] = {}) {
  const tree = <Harness {...options} />

  return render(tree)
}

function renderStrictHarness() {
  return render(
    <StrictMode>
      <Harness />
    </StrictMode>
  )
}

async function waitForStatus(where: string, status: string) {
  await waitFor(() => {
    expect(screen.getByLabelText(`${where}: статус`).textContent).toBe(status)
  })
}

async function waitForProfile(where: string, name: string) {
  await waitFor(() => {
    expect(screen.getByLabelText(`${where}: профиль`).textContent).toBe(name)
  })
}

/** Ручной promise для сценария «ответ пришёл после смены пользователя». */
function deferred(): { promise: Promise<void>; resolve: () => void } {
  let settle: () => void = () => {}
  const promise = new Promise<void>((next) => {
    settle = next
  })

  return { promise, resolve: () => settle() }
}

describe('ProfileProvider', () => {
  it('аноним не делает ни одного GET users/me', async () => {
    const gets = countRequests('/api/users/me')

    renderHarness({ initialStatus: 'anonymous', initialUser: null })

    await waitForStatus('шапка', 'idle')

    expect(gets()).toBe(0)
  })

  it('в unavailable не делает запрос и держит профиль пустым', async () => {
    const gets = countRequests('/api/users/me')

    renderHarness({ initialStatus: 'unavailable', initialUser: null })

    await waitForStatus('шапка', 'idle')

    expect(gets()).toBe(0)
    expect(screen.getByLabelText('шапка: профиль').textContent).toBe('нет')
  })

  it('авторизованному делает ровно один GET users/me', async () => {
    const gets = countRequests('/api/users/me')

    server.use(http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))))

    renderHarness()

    await waitForStatus('шапка', 'ready')

    expect(gets()).toBe(1)
    await waitForProfile('кабинет', 'Марк Правкин')
  })

  it('под StrictMode делает ровно один GET users/me', async () => {
    const gets = countRequests('/api/users/me')

    server.use(http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))))

    renderStrictHarness()

    await waitForStatus('шапка', 'ready')

    expect(gets()).toBe(1)
  })

  it('повторный рендер провайдера не делает второй GET users/me', async () => {
    const user = userEvent.setup()
    const gets = countRequests('/api/users/me')

    server.use(http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))))

    renderHarness()

    await waitForStatus('шапка', 'ready')
    await user.click(screen.getByRole('button', { name: 'Лишний рендер' }))

    expect(screen.getByLabelText('шапка: статус').textContent).toBe('ready')
    expect(gets()).toBe(1)
  })

  it('setAvatar меняет аватар у всех подписчиков без второго GET', async () => {
    const user = userEvent.setup()
    const gets = countRequests('/api/users/me')

    server.use(http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))))

    renderHarness()

    await waitForStatus('шапка', 'ready')

    await user.click(screen.getByRole('button', { name: 'Новый аватар' }))

    await waitFor(() => {
      expect(screen.getByLabelText('шапка: аватар').textContent).toBe(
        AVATAR_URL
      )
      expect(screen.getByLabelText('кабинет: аватар').textContent).toBe(
        AVATAR_URL
      )
    })
    expect(gets()).toBe(1)

    // Удаление в кабинете так же сразу возвращает инициалы обеим поверхностям.
    await user.click(screen.getByRole('button', { name: 'Убрать аватар' }))

    await waitFor(() => {
      expect(screen.getByLabelText('шапка: аватар').textContent).toBe('нет')
      expect(screen.getByLabelText('кабинет: аватар').textContent).toBe('нет')
    })
  })

  it('setAvatar снимает флаг блокировки у всех подписчиков', async () => {
    const user = userEvent.setup()

    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json(
          profile(USER.id, 'Марк', {
            avatarUrl: AVATAR_URL,
            isAvatarBlocked: true,
          })
        )
      )
    )

    renderHarness()

    await waitForStatus('шапка', 'ready')
    expect(screen.getByLabelText('шапка: блокировка').textContent).toBe('да')

    await user.click(screen.getByRole('button', { name: 'Новый аватар' }))

    await waitFor(() => {
      expect(screen.getByLabelText('шапка: блокировка').textContent).toBe('нет')
      expect(screen.getByLabelText('кабинет: блокировка').textContent).toBe('нет')
    })
  })

  it('save отправляет сырой avatarUrl и сохраняет флаг блокировки', async () => {
    const user = userEvent.setup()
    let body: Record<string, unknown> | null = null

    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json(
          profile(USER.id, 'Марк', {
            avatarUrl: AVATAR_URL,
            isAvatarBlocked: true,
          })
        )
      ),
      http.put(`/api/users/${USER.id}/profile`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>

        return noContent()
      })
    )

    renderHarness()

    await waitForStatus('шапка', 'ready')
    await user.click(screen.getByRole('button', { name: 'Сохранить профиль' }))
    await waitForProfile('кабинет', 'Марк Правкин')

    // Сырой URL обязателен: `null` стёр бы фото при сохранении имени.
    expect(body).toStrictEqual({
      firstName: 'Марк',
      lastName: 'Правкин',
      phoneNumber: null,
      avatarUrl: AVATAR_URL,
    })
    expect(screen.getByLabelText('шапка: блокировка').textContent).toBe('да')
  })

  it('save обновляет профиль и не делает второй GET users/me', async () => {
    const user = userEvent.setup()
    const gets = countRequests('/api/users/me')
    let body: Record<string, unknown> | null = null

    server.use(
      http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))),
      http.put(`/api/users/${USER.id}/profile`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>

        return noContent()
      })
    )

    renderHarness()

    await waitForStatus('шапка', 'ready')
    await user.click(screen.getByRole('button', { name: 'Новый аватар' }))
    await user.click(screen.getByRole('button', { name: 'Сохранить профиль' }))

    await waitForProfile('кабинет', 'Марк Правкин')

    expect(body).toStrictEqual({
      firstName: 'Марк',
      lastName: 'Правкин',
      phoneNumber: null,
      avatarUrl: AVATAR_URL,
    })
    expect(gets()).toBe(1)
  })

  it('выход очищает профиль и не делает запрос', async () => {
    const user = userEvent.setup()
    const gets = countRequests('/api/users/me')

    server.use(http.get('/api/users/me', () => HttpResponse.json(profile(USER.id))))

    renderHarness()

    await waitForStatus('шапка', 'ready')
    await user.click(screen.getByRole('button', { name: 'Выйти' }))

    await waitForStatus('шапка', 'idle')
    expect(screen.getByLabelText('шапка: профиль').textContent).toBe('нет')
    expect(gets()).toBe(1)
  })

  it('смена user.id перезапрашивает профиль', async () => {
    const user = userEvent.setup()
    const gets = countRequests('/api/users/me')

    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json(
          gets() === 1
            ? profile(USER.id)
            : profile(OTHER_USER.id, 'Марина')
        )
      )
    )

    renderHarness()

    await waitForProfile('шапка', 'Марк Правкин')
    await user.click(screen.getByRole('button', { name: 'Сменить аккаунт' }))

    await waitForProfile('шапка', 'Марина Правкин')
    await waitForProfile('кабинет', 'Марина Правкин')
    expect(gets()).toBe(2)
  })

  it('поздний ответ прошлого пользователя не перезаписывает новый', async () => {
    const user = userEvent.setup()
    const firstStarted = deferred()
    const releaseFirst = deferred()
    let calls = 0

    server.use(
      http.get('/api/users/me', async () => {
        calls += 1

        if (calls === 1) {
          firstStarted.resolve()
          // Первый ответ «зависает», пока пользователь уже сменился.
          await releaseFirst.promise

          return HttpResponse.json(profile(USER.id, 'Марк'))
        }

        return HttpResponse.json(profile(OTHER_USER.id, 'Марина'))
      })
    )

    renderHarness()
    await firstStarted.promise

    await user.click(screen.getByRole('button', { name: 'Сменить аккаунт' }))
    await waitForProfile('шапка', 'Марина Правкин')

    releaseFirst.resolve()

    // Поздний ответ первого аккаунта не должен вернуть «Марк».
    await waitFor(() => {
      expect(screen.getByLabelText('шапка: профиль').textContent).toBe(
        'Марина Правкин'
      )
    })
  })

  it('401 даёт requiresSignIn, пустой error и не разлогинивает сам', async () => {
    const user = userEvent.setup()

    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json(
          problemDetails(401, { errorCodes: ['auth.required'] }),
          { status: 401 }
        )
      )
    )

    renderHarness()

    await waitForStatus('шапка', 'error')

    expect(screen.getByLabelText('шапка: вход').textContent).toBe('нужен')
    expect(screen.getByLabelText('шапка: профиль').textContent).toBe('нет')
    // Ошибка в шапке не показывается: сессией владеет SessionProvider.
    expect(screen.getByLabelText('шапка: ошибка').textContent).toBe('нет')

    await user.click(screen.getByRole('button', { name: 'Обновить профиль' }))

    await waitFor(() => {
      expect(screen.getByLabelText('шапка: вход').textContent).toBe('нужен')
    })
  })

  it('прочие ошибки отдаёт текстом из describeApiFailure', async () => {
    server.use(
      http.get('/api/users/me', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderHarness()

    await waitForStatus('шапка', 'error')

    expect(screen.getByLabelText('шапка: ошибка').textContent).toBe(
      'Не удалось загрузить профиль.'
    )
    expect(screen.getByLabelText('шапка: вход').textContent).toBe('не нужен')
  })
})
