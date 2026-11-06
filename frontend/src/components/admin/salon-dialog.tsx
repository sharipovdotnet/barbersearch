import { SalonDialogForm } from '@/components/salon/salon-form'
import type { AdminSalon } from '@/lib/admin-catalogue'

/**
 * Создание и правка салона администратором.
 *
 * Поля и валидация общие с кабинетом барбера (`components/salon/salon-form.tsx`),
 * а поведение прежнее: `createSalon` на создание и два независимых `PUT`
 * (`updateSalonLocation` + `updateSalonContactInfo`) на правку. Название задаётся
 * только при создании: в домене нет переименования.
 */
export function SalonDialog({
  mode,
  salon,
  onClose,
  onSaved,
  onContactsFailed,
}: {
  mode: 'create' | 'edit'
  salon: AdminSalon | null
  onClose: () => void
  onSaved: (message: string) => void
  onContactsFailed: () => void
}) {
  return (
    <SalonDialogForm
      scope="admin"
      mode={mode}
      salon={salon}
      salonId={salon?.id ?? null}
      onClose={onClose}
      onSaved={onSaved}
      onContactsFailed={onContactsFailed}
    />
  )
}
