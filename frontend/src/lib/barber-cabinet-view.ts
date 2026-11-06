/**
 * Вкладки кабинета барбера.
 *
 * Три вида на одном `tablist`: лента дня и два серверных списка. Общие имена
 * живут здесь, а не в компоненте, чтобы Fast Refresh не терял состояние формы
 * при правке файла с вкладками.
 */

export type BarberCabinetView = 'day' | 'upcoming' | 'past'

export const BARBER_CABINET_VIEWS: readonly {
  id: BarberCabinetView
  label: string
}[] = [
  { id: 'day', label: 'Сегодня' },
  { id: 'upcoming', label: 'Ближайшие' },
  { id: 'past', label: 'История' },
]

/** Панель одна на все три вкладки: содержимое меняется внутри неё. */
export const BARBER_CABINET_PANEL_ID = 'barber-bookings-panel'

export function barberViewTabId(view: BarberCabinetView): string {
  return `barber-view-tab-${view}`
}
