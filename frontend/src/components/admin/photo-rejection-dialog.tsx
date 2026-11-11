import { useState } from 'react'
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
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from '@/components/ui/field'
import { Textarea } from '@/components/ui/textarea'
import { useI18n } from '@/i18n'
import {
  PHOTO_REJECTION_MAX_LENGTH,
  validatePhotoRejectionReason,
} from '@/lib/photo-moderation'

/**
 * Отказ по фотографии: общий диалог админских карточек аватара и портфолио.
 *
 * `alertdialog`, потому что решение оставляет чужой кадр непубличным. Причина
 * обязательна и проверяется до запроса: пустую строку сервер всё равно отклонит.
 * Ошибка сервера (`409 «уже проверено»`, `400` причина) остаётся в диалоге, пока
 * его не закроют, — решение можно повторить или отменить.
 *
 * Фокус после закрытия возвращает вызывающий компонент: кнопка «Отклонить»
 * остаётся на странице, и растерять её фокус нельзя.
 */
export function PhotoRejectionDialog({
  target,
  onClose,
  onConfirm,
}: {
  /** Кто или что отклоняется: имя владельца либо подпись кадра. */
  target: string
  onClose: () => void
  /** `null` — успех; строка — русский текст ошибки, диалог остаётся открытым. */
  onConfirm: (reason: string) => Promise<string | null>
}) {
  const { t } = useI18n()
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [serverError, setServerError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function handleConfirm(): Promise<void> {
    if (pending) {
      return
    }

    const validation = validatePhotoRejectionReason(reason)

    if (validation !== null) {
      setReasonError(validation)
      return
    }

    setPending(true)
    setReasonError(null)
    setServerError(null)

    const failure = await onConfirm(reason.trim())

    setPending(false)

    if (failure === null) {
      onClose()
      return
    }

    setServerError(failure)
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
          <DialogTitle>{t('Отклонить фото?')}</DialogTitle>
          <DialogDescription>{target}</DialogDescription>
        </DialogHeader>

        <p className="text-sm text-muted-foreground">
          {t(
            'Фото останется непубличным. Владелец увидит причину и сможет загрузить другое.'
          )}
        </p>

        <Field data-invalid={Boolean(reasonError)}>
          <FieldLabel htmlFor="photo-rejection-reason">
            {t('Причина отказа')}
          </FieldLabel>
          <Textarea
            id="photo-rejection-reason"
            name="reason"
            rows={3}
            maxLength={PHOTO_REJECTION_MAX_LENGTH}
            value={reason}
            disabled={pending}
            aria-invalid={Boolean(reasonError)}
            aria-describedby={
              reasonError
                ? 'photo-rejection-reason-hint photo-rejection-reason-error'
                : 'photo-rejection-reason-hint'
            }
            onChange={(event) => {
              setReason(event.target.value)
              setReasonError(null)
              setServerError(null)
            }}
          />
          <FieldDescription id="photo-rejection-reason-hint">
            {t(
              'До {max} символов. Причина не редактируется после отказа.',
              { max: PHOTO_REJECTION_MAX_LENGTH }
            )}
          </FieldDescription>
          <FieldError id="photo-rejection-reason-error">
            {reasonError}
          </FieldError>
        </Field>

        {serverError && (
          <p role="alert" className="text-sm text-destructive">
            {serverError}
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
            {t('Отмена')}
          </Button>
          <Button
            type="button"
            variant="destructive"
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
            {t('Отклонить')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
