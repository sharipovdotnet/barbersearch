import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { SessionProvider } from '@/components/session-provider'
import { useSession, type SessionStatus } from '@/hooks/use-session'
import {
  noContent,
  problemDetails,
  testUser,
  unauthorized,
} from '@/test/msw/handlers'
import { countRequests } from '@/test/msw/requests'
import { server } from '@/test/msw/server'

const HINT_COOKIE = 'barbersearch.remember'

/**
 * Пробник вместо экрана: тест смотрит только на состояние сессии.
 * Кнопки дергают контекст напрямую, чтобы не тащить сюда диалог входа (§6.4).
 */
function SessionProbe() {
  const { status, user, signIn, signOut, retrySession } = useSession()

  return (
    <div>
      <output aria-label="Статус сессии">{status}</output>
      <output aria-label="Email">{user?.email ?? 'нет'}</output>
      <button type="button" onClick={retrySession}>
        Повторить
      </button>
      <button
        type="button"
        onClick={() => {
          void signIn({
            email: testUser.email,
            password: 'secret-pass',
            rememberMe: true,
          })
        }}
      >
        Войти (запомнить)
      </button>
      <button
        type="button"
        onClick={() => {
          void signIn({
            email: testUser.email,
            password: 'secret-pass',
            rememberMe: false,
          })
        }}
      >
        Войти (не запоминать)
      </button>
      <button
        type="button"
        onClick={() => {
          void signOut()
        }}
      >
        Выйти в тесте
      </button>
    </div>
  )
}

function renderProbe(options: { strict?: boolean } = {}) {
  const tree = (
    <SessionProvider>
      <SessionProbe />
    </SessionProvider>
  )

  render(options.strict ? <StrictMode>{tree}</StrictMode> : tree)
}

async function waitForStatus(status: SessionStatus) {
  await waitFor(() => {
    expect(screen.getByLabelText('Статус сессии').textContent).toBe(status)
  })
}

function setRememberHint() {
  document.cookie = `${HINT_COOKIE}=1; Path=/`
}

function hasRememberHint(): boolean {
  return document.cookie.split(';').some((part) => {
    return part.trim().startsWith(`${HINT_COOKIE}=1`)
  })
}

/** `401` refresh — терминальный отказ: токен мёртв, сессию нужно начинать заново. */
function refreshFailure(status: number, errorCodes: string[]) {
  return HttpResponse.json(
    problemDetails(status, { errorCodes, detail: 'Server diagnostic text.' }),
    { status }
  )
}

describe('SessionProvider', () => {
  it('на 200 от /auth/me переходит в authenticated с email', async () => {
    server.use(http.get('/api/auth/me', () => HttpResponse.json(testUser)))

    renderProbe()
    await waitForStatus('authenticated')

    expect(screen.getByLabelText('Email').textContent).toBe(testUser.email)
  })

  it('на 401 без подсказки «Запомнить меня» переходит в anonymous без refresh', async () => {
    const refreshRequests = countRequests('/api/auth/refresh')

    renderProbe()
    await waitForStatus('anonymous')

    expect(refreshRequests()).toBe(0)
    expect(screen.getByLabelText('Email').textContent).toBe('нет')
  })

  it('на 401 с подсказкой продлевает сессию и повторяет /auth/me', async () => {
    let meCalls = 0
    const meRequests = countRequests('/api/auth/me')
    const refreshRequests = countRequests('/api/auth/refresh')

    server.use(
      http.get('/api/auth/me', () => {
        meCalls += 1

        return meCalls === 1 ? unauthorized() : HttpResponse.json(testUser)
      })
    )

    setRememberHint()
    renderProbe()
    await waitForStatus('authenticated')

    expect(meRequests()).toBe(2)
    expect(refreshRequests()).toBe(1)
    expect(screen.getByLabelText('Email').textContent).toBe(testUser.email)
  })

  it('на терминальный refresh (401) переходит в anonymous и снимает подсказку', async () => {
    server.use(
      http.post('/api/auth/refresh', () =>
        refreshFailure(401, ['auth.refresh.expired'])
      )
    )

    setRememberHint()
    renderProbe()
    await waitForStatus('anonymous')

    expect(hasRememberHint()).toBe(false)
  })

  it('на 5xx при refresh переходит в unavailable и сохраняет подсказку', async () => {
    server.use(
      http.post('/api/auth/refresh', () =>
        HttpResponse.json(problemDetails(503), { status: 503 })
      )
    )

    setRememberHint()
    renderProbe()
    await waitForStatus('unavailable')

    expect(hasRememberHint()).toBe(true)
  })

  it('на сетевой сбой при refresh переходит в unavailable и сохраняет подсказку', async () => {
    server.use(http.post('/api/auth/refresh', () => HttpResponse.error()))

    setRememberHint()
    renderProbe()
    await waitForStatus('unavailable')

    expect(hasRememberHint()).toBe(true)
  })

  it('на сетевой сбой /auth/me переходит в unavailable, а не в anonymous', async () => {
    server.use(http.get('/api/auth/me', () => HttpResponse.error()))

    renderProbe()
    await waitForStatus('unavailable')

    expect(screen.getByLabelText('Email').textContent).toBe('нет')
  })

  it('на 5xx от /auth/me переходит в unavailable, а не в anonymous', async () => {
    server.use(
      http.get('/api/auth/me', () =>
        HttpResponse.json(problemDetails(500), { status: 500 })
      )
    )

    renderProbe()
    await waitForStatus('unavailable')
  })

  it('повторяет bootstrap из unavailable и переходит в authenticated', async () => {
    const user = userEvent.setup()
    let meCalls = 0

    server.use(
      http.get('/api/auth/me', () => {
        meCalls += 1

        return meCalls === 1 ? HttpResponse.error() : HttpResponse.json(testUser)
      })
    )

    renderProbe()
    await waitForStatus('unavailable')

    await user.click(screen.getByRole('button', { name: 'Повторить' }))
    await waitForStatus('authenticated')

    expect(screen.getByLabelText('Email').textContent).toBe(testUser.email)
  })

  it('после signIn переходит в authenticated, после signOut — в anonymous', async () => {
    const user = userEvent.setup()
    let signedIn = false

    server.use(
      http.post('/api/auth/login/cookie', () => {
        signedIn = true
        return noContent()
      }),
      http.get('/api/auth/me', () =>
        signedIn ? HttpResponse.json(testUser) : unauthorized()
      )
    )

    renderProbe()
    await waitForStatus('anonymous')

    await user.click(screen.getByRole('button', { name: 'Войти (не запоминать)' }))
    await waitForStatus('authenticated')

    await user.click(screen.getByRole('button', { name: 'Выйти в тесте' }))
    await waitForStatus('anonymous')

    expect(screen.getByLabelText('Email').textContent).toBe('нет')
  })

  it('вход с rememberMe ставит cookie-подсказку barbersearch.remember=1', async () => {
    const user = userEvent.setup()
    let signedIn = false

    server.use(
      http.post('/api/auth/login/cookie', () => {
        signedIn = true
        return noContent()
      }),
      http.get('/api/auth/me', () =>
        signedIn ? HttpResponse.json(testUser) : unauthorized()
      )
    )

    renderProbe()
    await waitForStatus('anonymous')

    await user.click(screen.getByRole('button', { name: 'Войти (запомнить)' }))
    await waitForStatus('authenticated')

    await waitFor(() => {
      expect(hasRememberHint()).toBe(true)
    })
  })

  it('вход без rememberMe снимает чужую cookie-подсказку', async () => {
    const user = userEvent.setup()
    let signedIn = false

    server.use(
      http.post('/api/auth/login/cookie', () => {
        signedIn = true
        return noContent()
      }),
      http.get('/api/auth/me', () =>
        signedIn ? HttpResponse.json(testUser) : unauthorized()
      )
    )

    renderProbe()
    await waitForStatus('anonymous')

    // Подсказка могла остаться от прошлой сессии — вход без «Запомнить меня» её снимает.
    setRememberHint()

    await user.click(screen.getByRole('button', { name: 'Войти (не запоминать)' }))
    await waitForStatus('authenticated')

    expect(hasRememberHint()).toBe(false)
  })

  it('после выхода подсказки нет', async () => {
    const user = userEvent.setup()
    let signedIn = false

    server.use(
      http.post('/api/auth/login/cookie', () => {
        signedIn = true
        return noContent()
      }),
      http.get('/api/auth/me', () =>
        signedIn ? HttpResponse.json(testUser) : unauthorized()
      )
    )

    renderProbe()
    await waitForStatus('anonymous')

    await user.click(screen.getByRole('button', { name: 'Войти (запомнить)' }))
    await waitForStatus('authenticated')
    expect(hasRememberHint()).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Выйти в тесте' }))
    await waitForStatus('anonymous')

    expect(hasRememberHint()).toBe(false)
  })

  it('под StrictMode делает ровно один /auth/me на bootstrap', async () => {
    const meRequests = countRequests('/api/auth/me')

    renderProbe({ strict: true })
    await waitForStatus('anonymous')

    expect(meRequests()).toBe(1)
  })
})
