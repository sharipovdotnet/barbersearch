import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  AUTO_REFRESH_INTERVAL_MS,
  useAutoRefresh,
} from '@/hooks/use-auto-refresh'

/**
 * Таймер опроса: период, пауза на скрытой вкладке и отсутствие наложений.
 *
 * Проверяется сам хук, а не экран: кабинет лишь передаёт ему колбэк тихой
 * перезагрузки. jsdom по умолчанию считает документ видимым, поэтому скрытость
 * подменяется геттером и событием `visibilitychange`.
 */

function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    get: () => state,
  })
  document.dispatchEvent(new Event('visibilitychange'))
}

afterEach(() => {
  vi.useRealTimers()
  // Удаляем собственный геттер — возвращается настоящий из прототипа jsdom.
  Reflect.deleteProperty(document, 'visibilityState')
})

describe('useAutoRefresh', () => {
  it('первый опрос — через период, а не при монтировании', () => {
    vi.useFakeTimers()
    const onRefresh = vi.fn()

    renderHook(() => useAutoRefresh({ enabled: true, onRefresh }))

    expect(onRefresh).not.toHaveBeenCalled()

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS - 1)
    })
    expect(onRefresh).not.toHaveBeenCalled()

    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('на скрытой вкладке молчит, а при возврате обновляет сразу', async () => {
    vi.useFakeTimers()
    const onRefresh = vi.fn()

    renderHook(() => useAutoRefresh({ enabled: true, onRefresh }))

    act(() => {
      setVisibility('hidden')
    })
    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS * 3)
    })
    expect(onRefresh).not.toHaveBeenCalled()

    act(() => {
      setVisibility('visible')
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)

    // Тик снимает флаг «в полёте» в микрозадаче: без этого следующий период
    // считался бы наложением.
    await act(async () => {
      await Promise.resolve()
    })

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS)
    })
    expect(onRefresh).toHaveBeenCalledTimes(2)
  })

  it('не запускает второй тик, пока первый не завершился', async () => {
    vi.useFakeTimers()
    let finish: () => void = () => {}
    const onRefresh = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve
        })
    )

    renderHook(() => useAutoRefresh({ enabled: true, onRefresh }))

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)

    await act(async () => {
      finish()
      await Promise.resolve()
    })

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS)
    })
    expect(onRefresh).toHaveBeenCalledTimes(2)
  })

  it('без периода не тикает, но обновляет при возврате на вкладку', async () => {
    vi.useFakeTimers()
    const onRefresh = vi.fn()

    renderHook(() =>
      useAutoRefresh({ enabled: true, poll: false, onRefresh })
    )

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS * 3)
    })
    expect(onRefresh).not.toHaveBeenCalled()

    act(() => {
      setVisibility('hidden')
    })
    act(() => {
      setVisibility('visible')
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)

    await act(async () => {
      await Promise.resolve()
    })

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS * 2)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('с выключенным опросом не тикает и снимает таймер при размонтировании', () => {
    vi.useFakeTimers()
    const onRefresh = vi.fn()

    const { unmount, rerender } = renderHook(
      ({ enabled }) => useAutoRefresh({ enabled, onRefresh }),
      { initialProps: { enabled: false } }
    )

    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS * 2)
    })
    expect(onRefresh).not.toHaveBeenCalled()

    rerender({ enabled: true })
    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)

    unmount()
    act(() => {
      vi.advanceTimersByTime(AUTO_REFRESH_INTERVAL_MS * 2)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })
})
