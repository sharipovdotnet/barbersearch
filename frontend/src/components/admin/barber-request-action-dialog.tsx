import { useState } from 'react'
import { Loader2, TriangleAlert } from 'lucide-react'

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
import { useI18n } from '@/i18n'
import {
  BARBER_REQUEST_REJECTION_MAX_LENGTH,
  type AdminBarberRequest,
  type AdminBarberRequestAction,
} from '@/lib/admin-barber-requests'

/**
 * Подтверждение решения по заявке: `alertdialog`, потому что оба действия меняют
 * чужой доступ и не отменяются одним кликом.
 *
 * Одобрение объясняет последствие до запроса: `ChangeRole` ротирует security stamp,
 * поэтому все сессии заявителя завершаются, а профиль барбера он создаст при
 * следующем входе. Отказ требует причину: пустую строку сервер отклонит, но
 * отправлять её незачем — форма проверяет до запроса.
 */
export function BarberRequestActionDialog({
  kind,
  request,
  onClose,
  onConfirm,
}: {
  kind: AdminBarberRequestAction
  request: AdminBarberRequest
  onClose: () => void
  /** `null` — успех; строка — русский текст ошибки, диалог остаётся открытым. */
  onConfirm: (reason: string | null) => Promise<string | null>
}) {
  const { t } = useI18n()
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isApprove = kind === 'approve'

  async function handleConfirm() {
    if (pending) {
      return
    }

    if (!isApprove) {
      const trimmed = reason.trim()

      if (trimmed === '') {
        setReasonError(t('Укажите причину отказа.'))
        return
      }

      if (trimmed.length > BARBER_REQUEST_REJECTION_MAX_LENGTH) {
        setReasonError(
          t('Слишком длинная причина — максимум {max} символов.', {
            max: BARBER_REQUEST_REJECTION_MAX_LENGTH,
          })
        )
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
            {isApprove ? t('Одобрить заявку?') : t('Отклонить заявку?')}
          </DialogTitle>
          <DialogDescription>
            {request.displayName} · {request.applicant.email}
          </DialogDescription>
        </DialogHeader>

        {isApprove ? (
          <div className="flex gap-2.5 rounded-lg bg-warning/10 p-3">
            <TriangleAlert
              className="mt-0.5 size-4 shrink-0 text-warning"
              aria-hidden
            />
            <p className="text-sm text-foreground/80">
              {t(
                'Пользователь получит роль барбера. Смена роли завершит все его сессии — он войдёт заново, и после входа создаст профиль барбера. Пока профиля нет, клиенты его не видят.'
              )}
            </p>
          </div>
        ) : (
          <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
            <TriangleAlert
              className="mt-0.5 size-4 shrink-0 text-destructive"
              aria-hidden
            />
            <p className="text-sm text-foreground/80">
              {t(
                'Заявитель увидит причину в своём статусе и сможет подать новую заявку.'
              )}
            </p>
          </div>
        )}

        {!isApprove && (
          <Field data-invalid={Boolean(reasonError)}>
            <FieldLabel htmlFor="barber-request-rejection-reason">
              {t('Причина отказа')}
            </FieldLabel>
            <Textarea
              id="barber-request-rejection-reason"
              name="reason"
              rows={3}
              maxLength={BARBER_REQUEST_REJECTION_MAX_LENGTH}
              value={reason}
              disabled={pending}
              aria-invalid={Boolean(reasonError)}
              aria-describedby={
                reasonError
                  ? 'barber-request-rejection-reason-hint barber-request-rejection-reason-error'
                  : 'barber-request-rejection-reason-hint'
              }
              onChange={(event) => {
                setReason(event.target.value)
                setReasonError(null)
                setError(null)
              }}
            />
            <FieldDescription id="barber-request-rejection-reason-hint">
              {t(
                'До {max} символов. Причина не редактируется после отказа.',
                { max: BARBER_REQUEST_REJECTION_MAX_LENGTH }
              )}
            </FieldDescription>
            <FieldError id="barber-request-rejection-reason-error">
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
            {t('Оставить как есть')}
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
            {isApprove ? t('Одобрить') : t('Отклонить')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
