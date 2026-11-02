import { useCallback } from 'react'

import { usePagedList, type PagedList } from '@/hooks/use-paged-list'
import {
  describeAdminAuditError,
  fetchAdminAuditEntries,
  type AdminAuditEntry,
  type AdminAuditEntityType,
} from '@/lib/admin-audit'

/** Строка журнала опознаётся по id записи аудита. */
const getEntryId = (entry: AdminAuditEntry): string => entry.id

/** Ошибка первой страницы; на ней список пуст. */
const LIST_FALLBACK = 'Не удалось загрузить журнал.'
/** Ошибка следующей страницы; текущая остаётся на экране. */
const PAGE_FALLBACK = 'Не удалось загрузить следующую страницу журнала.'

export type AdminAuditListController = PagedList<AdminAuditEntry>

/**
 * Список журнала выбранного типа сущности, автора и периода.
 *
 * Здесь пейджер обязателен: журнал растёт монотонно, и это не каталог-справочник.
 * Страницы, кэш, отмена запросов и тихое обновление живут в общем `usePagedList`;
 * `fetchPage` меняется вместе с фильтрами, поэтому смена любого фильтра сама
 * начинает первую страницу и отсекает ответ прошлого запроса.
 */
export function useAdminAuditList({
  entityType,
  actor,
  from,
  to,
}: {
  entityType: AdminAuditEntityType
  actor: string
  from: string
  to: string
}): AdminAuditListController {
  const fetchPage = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      fetchAdminAuditEntries({
        entityType,
        actor: actor || undefined,
        from: from || undefined,
        to: to || undefined,
        cursor,
        signal,
      }),
    [entityType, actor, from, to]
  )

  return usePagedList<AdminAuditEntry>({
    getId: getEntryId,
    fetchPage,
    fallback: LIST_FALLBACK,
    pageFallback: PAGE_FALLBACK,
    requiresAuth: true,
    mapError: describeAdminAuditError,
  })
}
