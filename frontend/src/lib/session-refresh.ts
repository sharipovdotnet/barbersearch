import { ApiError } from '@/lib/api'
import { requestSessionRefresh } from '@/lib/auth'
import {
  clearRememberHint,
  hasRememberHint,
  markRememberedSession,
} from '@/lib/session-hint'

/**
 * Продление cookie-сессии.
 *
 * Refresh одноразовый: сервер ротирует токен на каждом вызове, а повторное предъявление
 * заменённого токена отзывает всю цепочку (`auth.refresh.reused`). Отсюда два уровня защиты —
 * single-flight внутри вкладки и Web Locks между вкладками.
 */

export type RefreshOutcome =
  | { kind: 'refreshed' }
  | { kind: 'terminal' }
  | { kind: 'transient'; error: ApiError }

const LOCK_NAME = 'barbersearch:session-refresh'
const LOCK_TIMEOUT_MS = 10_000

let inFlight: Promise<RefreshOutcome> | null = null
let isTerminal = false

/** Новый вход начинает новую сессию: снимает липкое терминальное состояние. */
export function resetSessionRefresh(): void {
  isTerminal = false
}

/**
 * Продлить сессию; параллельные `401` ждут один и тот же вызов.
 *
 * Терминальное состояние липкое: после мёртвого refresh-токена модуль молчит, пока сессия
 * не начнётся заново, иначе шторм `401` устроил бы шторм refresh.
 */
export function refreshSession(): Promise<RefreshOutcome> {
  if (isTerminal) {
    return Promise.resolve({ kind: 'terminal' })
  }

  if (inFlight) {
    return inFlight
  }

  const attempt = runRefresh().finally(() => {
    inFlight = null
  })
  inFlight = attempt

  return attempt
}

async function runRefresh(): Promise<RefreshOutcome> {
  // Соседний `401` мог отметить терминальное состояние, пока этот ждал очереди.
  if (isTerminal) {
    return { kind: 'terminal' }
  }

  const outcome = await withCrossTabLock(performRefresh)

  if (outcome.kind === 'terminal') {
    isTerminal = true
    clearRememberHint()
    return outcome
  }

  if (outcome.kind === 'refreshed' && hasRememberHint()) {
    // Сессию просили запомнить — держим подсказку на скользящие 14 дней.
    markRememberedSession()
  }

  return outcome
}

async function withCrossTabLock(
  task: () => Promise<RefreshOutcome>
): Promise<RefreshOutcome> {
  if (!navigator.locks) {
    // Старый браузер: без лока две вкладки могут предъявить один токен, и сервер отзовёт
    // цепочку. Внутри вкладки single-flight при этом продолжает работать.
    return task()
  }

  try {
    const granted: RefreshOutcome = await navigator.locks.request(
      LOCK_NAME,
      { mode: 'exclusive', signal: AbortSignal.timeout(LOCK_TIMEOUT_MS) },
      () => task()
    )

    return granted
  } catch {
    // Лок не дождались — не шлём refresh, иначе рискуем гонкой с соседней вкладкой.
    return {
      kind: 'transient',
      error: new ApiError(
        0,
        null,
        'Продление сессии не уложилось во время ожидания.'
      ),
    }
  }
}

async function performRefresh(): Promise<RefreshOutcome> {
  for (let attempt = 0; ; attempt += 1) {
    try {
      await requestSessionRefresh()
      return { kind: 'refreshed' }
    } catch (cause) {
      if (!(cause instanceof ApiError)) {
        return {
          kind: 'transient',
          error: new ApiError(0, null, 'Продление сессии не удалось.'),
        }
      }

      // Сервер отдаёт `401` только с публичным кодом `auth.refresh.*`: токен мёртв,
      // повтор не поможет, сессию нужно начинать заново.
      if (cause.status === 401) {
        return { kind: 'terminal' }
      }

      // Устаревший antiforgery-токен лечится одним повтором со свежим.
      if (
        attempt === 0 &&
        cause.status === 400 &&
        cause.hasCode('auth.csrf.invalid')
      ) {
        continue
      }

      return { kind: 'transient', error: cause }
    }
  }
}
