import { setupServer } from 'msw/node'

import { handlers } from '@/test/msw/handlers'

/**
 * Единственная точка перехвата сети в юнит-тестах: реальных запросов нет.
 *
 * `onUnhandledRequest: 'error'` задаётся в `src/test/setup.ts` — незамоканный запрос
 * должен валить тест, а не проходить случайно.
 */
export const server = setupServer(...handlers)
