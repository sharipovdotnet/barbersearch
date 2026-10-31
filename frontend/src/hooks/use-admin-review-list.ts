import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  describeAdminReviewListError,
  fetchAdminReviews,
  type AdminReview,
} from '@/lib/admin-reviews'
import type { AdminTab } from '@/lib/admin-path'

/** Ошибка первой страницы; на ней список пуст. */
const LIST_FALLBACK = 'Не удалось загрузить отзывы.'
/** Ошибка следующей страницы; текущая остаётся на экране. */
const PAGE_FALLBACK = 'Не удалось загрузить ещё отзывы.'

export type AdminReviewListController = PagedList<AdminReview>

/**
 * Отзывы выбранной вкладки: тонкая обёртка над общим контроллером страниц.
 *
 * `pending` и `published` — разные серверные списки, поэтому `fetchPage` меняется
 * вместе с вкладкой: общий хук сам сбрасывает кэш и грузит первую страницу, ручной
 * эффект на смену вкладки больше не нужен. `401` остаётся гейтом входа, а не общим
 * текстом ошибки.
 */
export function useAdminReviewList(tab: AdminTab): AdminReviewListController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchAdminReviews({ tab, cursor, signal }),
    [tab]
  )

  return usePagedList<AdminReview>({
    getId: (review) => review.id,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
    mapError: describeAdminReviewListError,
  })
}
