import { SalonDialogForm } from '@/components/salon/salon-form'
import type { BarberSalonSummary } from '@/lib/barber-salons'

/**
 * Диалог салона барбера: создание (`POST /api/salons/own`) и правка.
 *
 * Форма та же, что у админского каталога; отличается только путь создания и
 * подсказки. После правки отправляются два независимых `PUT` — адрес и контакты.
 */
export function BarberSalonDialog({
  mode,
  salon,
  initialFocus,
  onClose,
  onSaved,
  onContactsFailed,
}: {
  mode: 'create' | 'edit'
  salon: BarberSalonSummary | null
  initialFocus?: 'location' | 'contacts'
  onClose: () => void
  onSaved: (message: string) => void
  onContactsFailed?: () => void
}) {
  return (
    <SalonDialogForm
      scope="owner"
      mode={mode}
      salon={salon}
      salonId={salon?.salonId ?? null}
      initialFocus={initialFocus}
      onClose={onClose}
      onSaved={onSaved}
      onContactsFailed={onContactsFailed}
    />
  )
}
