import { useEffect, type ReactNode } from 'react'

import {
  AdminQueuesContext,
} from '@/hooks/use-admin-queues'
import { useAdminSummary } from '@/hooks/use-admin-summary'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { ADMIN_QUEUES_CHANGED_EVENT } from '@/lib/admin-queues'

/**
 * Один опрос сводки на всю админку.
 *
 * Сводка — единственное агрегатное чтение (`GET api/admin/summary`): из одного
 * снимка живут и рейл «Разделы», и экран «Сводка». Два независимых опроса дали бы
 * расхождение — 12 в рейле и 11 в блоке, — поэтому провайдер один, а переходы
 * между разделами не плодят запросы.
 *
 * Внутри — `useAdminSummary()` (отмена устаревших ответов, `401` как гейт входа,
 * ошибка обновления строкой) и `useAutoRefresh` (тик раз в 30 секунд, пауза на
 * скрытой вкладке, мгновенное обновление при возврате). Событие
 * `admin:queues-changed` из разделов делает тот же тихий `refresh()` сразу после
 * успешной мутации.
 */
export function AdminQueuesProvider({ children }: { children: ReactNode }) {
  const summary = useAdminSummary()
  const { refresh } = summary

  // Ошибка первой загрузки не опрашивается: у неё есть «Повторить».
  useAutoRefresh({
    enabled: summary.status === 'ready' && !summary.requiresSignIn,
    onRefresh: refresh,
  })

  useEffect(() => {
    const handleChange = () => refresh()

    window.addEventListener(ADMIN_QUEUES_CHANGED_EVENT, handleChange)

    return () => {
      window.removeEventListener(ADMIN_QUEUES_CHANGED_EVENT, handleChange)
    }
  }, [refresh])

  return (
    <AdminQueuesContext.Provider value={summary}>
      {children}
    </AdminQueuesContext.Provider>
  )
}
