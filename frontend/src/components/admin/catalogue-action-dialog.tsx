import { Loader2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  ADMIN_CATALOGUE_ENTITY_LABELS,
  type CatalogueTab,
} from '@/lib/admin-catalogue'

/** Строка, над которой выполняется действие: столько данных нужно для текста последствий. */
export type CatalogueActionRow = {
  id: string
  name: string
  isActive: boolean
  usageCount: number
}

/**
 * Подтверждение выключения и включения строки.
 *
 * Выключение объясняет последствие до запроса: строка исчезнет из витрины, но связи
 * барберов и история записей останутся. Для услуг и салонов показывается число активных
 * подтверждённых барберов — это `usageCount` из API, а не длина массива на экране.
 */
export function CatalogueActionDialog({
  tab,
  row,
  isPending,
  error,
  onClose,
  onConfirm,
}: {
  tab: CatalogueTab
  row: CatalogueActionRow
  isPending: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}) {
  const entity = ADMIN_CATALOGUE_ENTITY_LABELS[tab].toLowerCase()
  const isDeactivation = row.isActive

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        // Пока запрос в полёте, диалог не закрывается: результат должен быть виден.
        if (!open && !isPending) {
          onClose()
        }
      }}
    >
      <DialogContent
        role={isDeactivation ? 'alertdialog' : 'dialog'}
        className="sm:max-w-md"
      >
        <DialogHeader>
          <DialogTitle>
            {isDeactivation ? 'Выключить' : 'Включить'} {entity}?
          </DialogTitle>
          <DialogDescription>{row.name}</DialogDescription>
        </DialogHeader>

        <p className="text-sm text-muted-foreground">
          {isDeactivation
            ? deactivationConsequence(tab, row.usageCount)
            : 'Строка снова появится в фильтрах и на карточках.'}
        </p>

        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-8"
            disabled={isPending}
            onClick={onClose}
          >
            Отмена
          </Button>
          <Button
            type="button"
            variant={isDeactivation ? 'destructive' : 'default'}
            className="h-11 sm:h-8"
            disabled={isPending}
            aria-busy={isPending}
            onClick={onConfirm}
          >
            {isPending && (
              <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
            )}
            {isDeactivation ? 'Выключить' : 'Включить'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** «1 барбер» / «2 барбера» / «13 барберов». */
function barbers(count: number): string {
  const mod10 = count % 10
  const mod100 = count % 100

  if (mod10 === 1 && mod100 !== 11) {
    return `${count} барбер`
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return `${count} барбера`
  }

  return `${count} барберов`
}

function deactivationConsequence(tab: CatalogueTab, usageCount: number): string {
  switch (tab) {
    case 'services':
      return `Услуга исчезнет из фильтров и поиска, но останется в истории записей. Сейчас её используют ${barbers(usageCount)}.`
    case 'specializations':
      return 'Специализация исчезнет из фильтров витрины. Она останется на карточках барберов, которые её указали.'
    case 'salons':
      return `Салон исчезнет из поиска и с карточек. Барберы, которые работают только здесь, могут перестать показываться. Сейчас здесь работают ${barbers(usageCount)}.`
  }
}
