import { useEffect } from 'react'
import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import type { Page } from '@/lib/paging'

/**
 * Смена источника страниц (вкладка, фильтр, поиск) — это другой список.
 *
 * Хук обязан выбросить строки прошлого источника до того, как экран их увидит:
 * иначе автовыбор первой карточки берёт строку старого списка и открывает
 * «чужой» отзыв/профиль под новым заголовком (баг админки отзывов).
 */

type Snapshot = {
  status: PagedList<string>['status']
  items: string[]
}

function page(items: string[]): Page<string> {
  return { items, nextCursor: null }
}

/** Собирает только закоммиченные рендеры: их и видят эффекты экрана. */
function useList(
  fetchPage: (cursor: string | null, signal: AbortSignal) => Promise<Page<string>>,
  committed: Snapshot[]
): PagedList<string> {
  const list = usePagedList<string>({
    getId: (item) => item,
    fetchPage,
    fallback: 'Не удалось загрузить список.',
  })

  useEffect(() => {
    committed.push({ status: list.status, items: list.items })
  })

  return list
}

describe('usePagedList', () => {
  it('при смене источника не коммитит строки прошлого списка', async () => {
    const fetchPending = async () => page(['отзыв на модерации'])
    const fetchPublished = async () => page(['опубликованный отзыв'])
    const committed: Snapshot[] = []

    const { result, rerender } = renderHook(
      ({ fetchPage }) => useList(fetchPage, committed),
      { initialProps: { fetchPage: fetchPending } }
    )

    await waitFor(() => {
      expect(result.current.items).toEqual(['отзыв на модерации'])
    })

    const beforeSwitch = committed.length

    rerender({ fetchPage: fetchPublished })

    // Первый же закоммиченный рендер нового источника — загрузка без старых строк.
    expect(committed[beforeSwitch]).toEqual({ status: 'loading', items: [] })
    expect(
      committed
        .slice(beforeSwitch)
        .some((entry) => entry.items.includes('отзыв на модерации'))
    ).toBe(false)

    await waitFor(() => {
      expect(result.current.items).toEqual(['опубликованный отзыв'])
    })
  })
})
