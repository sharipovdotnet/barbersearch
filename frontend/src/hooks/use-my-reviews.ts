import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import { useI18n } from '@/i18n'
import { ApiError } from '@/lib/api'
import {
  deleteMyReview,
  fetchMyReviews,
  updateMyReview,
  type MyReview,
  type UpdateMyReviewInput,
} from '@/lib/my-reviews'

export type MyReviewsController = PagedList<MyReview> & {
  updateReview: (id: string, input: UpdateMyReviewInput) => Promise<void>
  deleteReview: (id: string) => Promise<'deleted' | 'alreadyDeleted'>
}

const LIST_FALLBACK = 'Не удалось загрузить отзывы.'
const PAGE_FALLBACK = 'Не удалось загрузить следующие отзывы.'

/**
 * Свои отзывы: страницы, правка и удаление.
 *
 * Список не растёт: одна страница на экране, «Назад»/«Дальше». Кэш страниц,
 * защита от двойного клика и политика пустой страницы живут в `usePagedList`.
 * Сортировка и размер страницы серверные, фильтров у списка нет, поэтому
 * `fetchPage` не меняется между рендерами.
 *
 * Правка и удаление меняют только свою строку: повторный `GET` сдвинул бы
 * пагинацию.
 */
export function useMyReviews(): MyReviewsController {
  const { t } = useI18n()

  // Смена идентичности `fetchPage` заставила бы хук перезагрузить первую страницу
  // и сбросить кэш, а зависимостей у списка нет.
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchMyReviews(cursor, signal),
    []
  )

  const list = usePagedList<MyReview>({
    getId: (review) => review.id,
    fetchPage,
    fallback: t(LIST_FALLBACK),
    pageFallback: t(PAGE_FALLBACK),
    requiresAuth: true,
  })

  const { items, replaceItem, removeItem } = list

  const updateReview = useCallback(
    async (id: string, input: UpdateMyReviewInput) => {
      const saved = await updateMyReview(id, input)

      const current = items.find((review) => review.id === id)

      // Строки уже нет на странице (например, страница сменилась) — менять нечего.
      if (current === undefined) {
        return
      }

      // Статус берём из ответа: правка текста опубликованного отзыва возвращает его
      // в очередь модерации, и бейдж «Опубликован» иначе врал бы до перезагрузки.
      // `createdAt` не меняется, поэтому строка остаётся на месте и порядок не едет.
      replaceItem(id, {
        ...current,
        rating: input.rating,
        text: input.text,
        isPublished: saved.isPublished,
        updatedAt: saved.updatedAt,
      })
    },
    [items, replaceItem]
  )

  const deleteReview = useCallback(
    async (id: string): Promise<'deleted' | 'alreadyDeleted'> => {
      let outcome: 'deleted' | 'alreadyDeleted' = 'deleted'

      try {
        await deleteMyReview(id)
      } catch (cause) {
        // Повторное удаление сервер отдаёт как 404: для UI строка уже удалена.
        if (cause instanceof ApiError && cause.status === 404) {
          outcome = 'alreadyDeleted'
        } else {
          throw cause
        }
      }

      // Опустевшую страницу закрывает сам хук: предыдущая, первая заново или
      // пустое состояние — экран этого не решает.
      removeItem(id)
      return outcome
    },
    [removeItem]
  )

  return { ...list, updateReview, deleteReview }
}
