import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  describeAdminUserListError,
  fetchAdminUsers,
  type AdminUserRoleFilter,
  type AdminUserRow,
  type AdminUserStatusFilter,
} from '@/lib/admin-users'

/** Строка списка опознаётся по id пользователя. */
const getUserId = (user: AdminUserRow): string => user.id

/** Ошибка первой страницы; на ней список пуст. */
const LIST_FALLBACK = 'Не удалось загрузить пользователей.'
/** Ошибка следующей страницы; текущая остаётся на экране. */
const PAGE_FALLBACK = 'Не удалось загрузить ещё пользователей.'

export type AdminUserListController = PagedList<AdminUserRow>

/**
 * Список пользователей выбранного статуса и роли с поиском.
 *
 * Страницы, кэш, отмена запросов и тихое обновление живут в общем `usePagedList`;
 * здесь только сборка запроса. `fetchPage` меняется вместе с фильтрами и поиском,
 * поэтому хук сам начинает с первой страницы и отсекает ответ прошлого запроса.
 */
export function useAdminUserList({
  status,
  role,
  search,
}: {
  status: AdminUserStatusFilter
  role: AdminUserRoleFilter
  search: string
}): AdminUserListController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchAdminUsers({
        status,
        role,
        search: search || undefined,
        cursor,
        signal,
      }),
    [status, role, search]
  )

  return usePagedList<AdminUserRow>({
    getId: getUserId,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
    mapError: describeAdminUserListError,
  })
}
