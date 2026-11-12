import { createContext, useContext } from 'react'

import type { AdminSummaryController } from '@/hooks/use-admin-summary'

/**
 * Снимок админской сводки, общий для рейла и экрана «Сводка».
 *
 * Контекст живёт отдельно от провайдера по той же причине, что и `SessionContext`
 * в `use-session.ts`: файл с компонентом должен экспортировать только компоненты,
 * иначе страдает fast refresh. Провайдер — `AdminQueuesProvider` в
 * `components/admin/admin-queues-provider.tsx`.
 */
export const AdminQueuesContext = createContext<AdminSummaryController | null>(
  null
)

/** Контроллер сводки. Вне провайдера — ошибка разработчика, а не пустой рейл. */
export function useAdminQueues(): AdminSummaryController {
  const context = useContext(AdminQueuesContext)

  if (!context) {
    throw new Error('useAdminQueues must be used inside AdminQueuesProvider')
  }

  return context
}
