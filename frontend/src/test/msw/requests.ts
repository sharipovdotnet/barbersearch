import { onTestFinished } from 'vitest'

import { server } from '@/test/msw/server'

type RequestEvent = { request: Request }

/**
 * Счётчик запросов к одному пути.
 *
 * «`401` без подсказки не делает refresh» и «StrictMode делает один `/auth/me`» —
 * это контракт, поэтому проверяется числом запросов в MSW, а не комментарием (§5).
 */
export function countRequests(pathname: string): () => number {
  let count = 0

  const listener = (event: RequestEvent) => {
    if (new URL(event.request.url).pathname === pathname) {
      count += 1
    }
  }

  server.events.on('request:start', listener)
  onTestFinished(() => {
    server.events.removeListener('request:start', listener)
  })

  return () => count
}

/** Пути всех запросов теста — для проверки «сети не было вовсе». */
export function recordRequests(): string[] {
  const paths: string[] = []

  const listener = (event: RequestEvent) => {
    paths.push(new URL(event.request.url).pathname)
  }

  server.events.on('request:start', listener)
  onTestFinished(() => {
    server.events.removeListener('request:start', listener)
  })

  return paths
}
