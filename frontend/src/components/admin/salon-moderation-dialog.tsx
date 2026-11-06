import { Loader2, TriangleAlert } from 'lucide-react'
import { useState } from 'react'

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
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'
import type { AdminSalon, SalonModerationAction } from '@/lib/admin-catalogue'
import {
  SALON_REJECTION_MAX_LENGTH,
  validateSalonRejectionReason,
} from '@/lib/salon-moderation'

/**
 * Подтверждение решения по салону: `alertdialog`, потому что одобрение делает салон
 * публичным, а отказ виден владельцу.
 *
 * Одобрение объясняет последствие до запроса. Отказ требует причину: пустую строку
 * сервер отклонит, но отправлять её незачем — форма проверяет до запроса. Ошибку
 * сервера (`400`, `409`) возвращает экран и показывает здесь, диалог не закрывается.
 */
export function SalonModerationDialog({
  action,
  salon,
  onClose,
  onConfirm,
}: {
  action: SalonModerationAction
  salon: AdminSalon
  onClose: () => void
  /** `null` — успех; строка — русский текст ошибки, диалог остаётся открытым. */
  onConfirm: (reason: string | null) => Promise<string | null>
}) {
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isApprove = action === 'approve'

  async function handleConfirm() {
    if (pending) {
      return
    }

    if (!isApprove) {
      const validationError = validateSalonRejectionReason(reason)
      setReasonError(validationError)

      if (validationError !== null) {
        return
      }
    }

    setPending(true)
    setError(null)
    setReasonError(null)

    const failure = await onConfirm(isApprove ? null : reason.trim())

    setPending(false)

    if (failure === null) {
      onClose()
      return
    }

    setError(failure)
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        // Пока запрос в полёте, диалог не закрывается: результат должен быть виден.
        if (!open && !pending) {
          onClose()
        }
      }}
    >
      <DialogContent role="alertdialog" className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>
            {isApprove ? 'Одобрить салон?' : 'Отклонить салон?'}
          </DialogTitle>
          <DialogDescription>
            {salon.name}
            {salon.ownerEmail ? ` · ${salon.ownerEmail}` : ''}
          </DialogDescription>
        </DialogHeader>

        {isApprove ? (
          <div className="flex gap-2.5 rounded-lg bg-secondary p-3">
            <TriangleAlert
              className="mt-0.5 size-4 shrink-0 text-foreground/70"
              aria-hidden
            />
            <p className="text-sm text-foreground/80">
              Салон появится в поиске и станет доступен для записи.
            </p>
          </div>
        ) : (
          <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
            <TriangleAlert
              className="mt-0.5 size-4 shrink-0 text-destructive"
              aria-hidden
            />
            <p className="text-sm text-foreground/80">
              Салон останется невидимым. Владелец увидит причину в «Моих салонах».
            </p>
          </div>
        )}

        {!isApprove && (
          <Field data-invalid={Boolean(reasonError)}>
            <FieldLabel htmlFor="salon-rejection-reason">
              Причина отказа
            </FieldLabel>
            <Textarea
              id="salon-rejection-reason"
              name="reason"
              rows={3}
              maxLength={SALON_REJECTION_MAX_LENGTH}
              value={reason}
              disabled={pending}
              aria-invalid={Boolean(reasonError)}
              aria-describedby={
                reasonError
                  ? 'salon-rejection-reason-hint salon-rejection-reason-error'
                  : 'salon-rejection-reason-hint'
              }
              onChange={(event) => {
                setReason(event.target.value)
                setReasonError(null)
                setError(null)
              }}
            />
            <FieldDescription id="salon-rejection-reason-hint">
              До {SALON_REJECTION_MAX_LENGTH} символов. Причина не редактируется
              после отказа.
            </FieldDescription>
            <FieldError id="salon-rejection-reason-error">
              {reasonError}
            </FieldError>
          </Field>
        )}

        {error && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}

        <DialogFooter className="flex-col sm:flex-row">
          <Button
            type="button"
            variant="outline"
            className="h-11 sm:h-8"
            disabled={pending}
            onClick={onClose}
          >
            Оставить как есть
          </Button>
          <Button
            type="button"
            variant={isApprove ? 'default' : 'destructive'}
            className="h-11 sm:h-8"
            disabled={pending}
            aria-busy={pending}
            onClick={() => {
              void handleConfirm()
            }}
          >
            {pending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {isApprove ? 'Одобрить' : 'Отклонить'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
