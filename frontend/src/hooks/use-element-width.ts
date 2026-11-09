import { useCallback, useRef, useState } from 'react'

/**
 * Фактическая ширина элемента в пикселях.
 *
 * Нужна там, где решение зависит не от окна, а от колонки: в split-view место
 * под список броней задают рейл, карточка справа и системный скроллбар, поэтому
 * один и тот же экран даёт разную ширину списка.
 *
 * Ссылка — callback-ref, а не `useRef`: список появляется только после ответа
 * API, и обычный ref не даёт повода перезапустить наблюдение. Ширина снимается
 * прямо в момент подключения узла, до отрисовки, чтобы список не успевал
 * мигнуть запасным видом. Пока ширины нет (`null`), вызывающий показывает
 * запасной вариант — так же ведёт себя jsdom в тестах, где `ResizeObserver`
 * отсутствует.
 */
export function useElementWidth<T extends HTMLElement>(): {
  ref: (node: T | null) => void
  width: number | null
} {
  const [width, setWidth] = useState<number | null>(null)
  const observerRef = useRef<ResizeObserver | null>(null)

  const ref = useCallback((node: T | null) => {
    observerRef.current?.disconnect()
    observerRef.current = null

    if (!node) {
      setWidth(null)

      return
    }

    setWidth(node.getBoundingClientRect().width)

    if (typeof ResizeObserver === 'undefined') {
      return
    }

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0]

      if (entry) {
        setWidth(entry.contentRect.width)
      }
    })

    observer.observe(node)
    observerRef.current = observer
  }, [])

  return { ref, width }
}
