import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, vi } from 'vitest'

import { setUnauthorizedHandler } from '@/lib/api'
import { resetSessionRefresh } from '@/lib/session-refresh'
import { server } from '@/test/msw/server'

/**
 * Общая обвязка юнит-тестов.
 *
 * Сеть — только MSW на границе HTTP: незамоканный запрос роняет тест. Модульное
 * состояние (`setUnauthorizedHandler`, липкий `terminal` в `session-refresh`,
 * cookie-подсказка «Запомнить меня») живёт дольше одного теста, поэтому сбрасывается
 * здесь, а не по договорённости внутри отдельных файлов (§8).
 */

// jsdom не реализует ни один из этих API, а Base UI и приложение их зовут.
window.matchMedia = (query: string): MediaQueryList => ({
  matches: false,
  media: query,
  onchange: null,
  addListener: () => {},
  removeListener: () => {},
  addEventListener: () => {},
  removeEventListener: () => {},
  dispatchEvent: () => false,
})

Element.prototype.scrollIntoView = () => {}

// jsdom-реализация `scrollTo` печатает «Not implemented» в консоль: заменяем no-op.
window.scrollTo = () => {}

function clearCookies(): void {
  for (const entry of document.cookie.split(';')) {
    const name = entry.split('=')[0]?.trim()

    if (!name) {
      continue
    }

    document.cookie = `${name}=; Path=/; Max-Age=0`
  }
}

beforeAll(() => {
  // В msw 3 опция называется `onUnhandledFrame` (в msw 2 была `onUnhandledRequest`).
  // Незамоканный запрос должен валить тест, а не проходить случайно.
  server.listen({ onUnhandledFrame: 'error' })
})

afterEach(() => {
  cleanup()
  server.resetHandlers()
  vi.unstubAllGlobals()
  clearCookies()
  resetSessionRefresh()
  setUnauthorizedHandler(null)
})

afterAll(() => {
  server.close()
})
