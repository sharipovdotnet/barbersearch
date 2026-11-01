import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'

import { refreshSession } from '@/lib/session-refresh'
import { server } from '@/test/msw/server'

/**
 * Продление сессии передаёт «Запомнить меня» на сервер.
 *
 * Подсказка `barbersearch.remember` — единственный источник этого признака: транспорт
 * `requestSessionRefresh` cookie не читает. Тело запроса проверяется в MSW-обработчике.
 */

function refreshBodies(): string[] {
  const bodies: string[] = []

  server.use(
    http.post('/api/auth/refresh', async ({ request }) => {
      bodies.push(await request.text())

      return new HttpResponse(null, { status: 204 })
    })
  )

  return bodies
}

describe('refreshSession', () => {
  it('с подсказкой «Запомнить меня» просит rememberMe: true', async () => {
    document.cookie = 'barbersearch.remember=1; Path=/'
    const bodies = refreshBodies()

    await expect(refreshSession()).resolves.toStrictEqual({ kind: 'refreshed' })

    expect(bodies).toHaveLength(1)
    expect(JSON.parse(bodies[0])).toStrictEqual({ rememberMe: true })
  })

  it('без подсказки просит rememberMe: false', async () => {
    const bodies = refreshBodies()

    await expect(refreshSession()).resolves.toStrictEqual({ kind: 'refreshed' })

    expect(bodies).toHaveLength(1)
    expect(JSON.parse(bodies[0])).toStrictEqual({ rememberMe: false })
  })
})
