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
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import type { BarberBooking } from '@/lib/barber-bookings'
import {
  describeBarberBookingFailure,
  type BarberActionFailure,
} from '@/lib/errors'
import { formatDateTime } from '@/lib/format'

/** Предел сервера (`MAX_BARBER_NOTE_LENGTH`); клиент повторяет его для счётчика. */
const MAX_NOTE_LENGTH = 1000

/**
 * Заметка барбера к записи.
 *
 * Содержимое монтируется на каждое открытие: текст прошлой записи не переезжает в
 * следующую, сбрасывать его эффектом не нужно. Пустое поле очищает заметку — так же,
 * как это делает сервер.
 */
export function BarberNoteDialog({
  booking,
  onClose,
  onSave,
}: {
  booking: BarberBooking | null
  onClose: () => void
  onSave: (id: string, note: string) => Promise<void>
}) {
  return (
    <Dialog
      open={booking !== null}
      onOpenChange={(next) => {
        if (!next) {
          onClose()
        }
      }}
    >
      {booking && (
        <DialogContent className="sm:max-w-md">
          <BarberNoteForm
            booking={booking}
            onClose={onClose}
            onSave={onSave}
          />
        </DialogContent>
      )}
    </Dialog>
  )
}

function BarberNoteForm({
  booking,
  onClose,
  onSave,
}: {
  booking: BarberBooking
  onClose: () => void
  onSave: (id: string, note: string) => Promise<void>
}) {
  const [note, setNote] = useState(booking.barberNote ?? '')
  const [isPending, setIsPending] = useState(false)
  const [failure, setFailure] = useState<BarberActionFailure | null>(null)

  const isTooLong = note.trim().length > MAX_NOTE_LENGTH

  async function handleSave() {
    if (isPending || isTooLong) {
      return
    }

    setIsPending(true)
    setFailure(null)

    try {
      await onSave(booking.id, note.trim())
      onClose()
    } catch (cause) {
      setFailure(
        describeBarberBookingFailure(cause, 'Не удалось сохранить заметку.')
      )
    } finally {
      setIsPending(false)
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Заметка к записи</DialogTitle>
        <DialogDescription>
          {formatDateTime(booking.startAt, booking.salon.timeZoneId)} ·{' '}
          {booking.customer.displayName}
        </DialogDescription>
      </DialogHeader>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="barber-note">
          Заметка{' '}
          <span className="font-normal text-muted-foreground">
            — видит только барбер
          </span>
        </Label>
        <Textarea
          id="barber-note"
          name="barber-note"
          value={note}
          maxLength={MAX_NOTE_LENGTH}
          disabled={isPending}
          placeholder="Что запомнить о визите"
          onChange={(event) => {
            setNote(event.target.value)
            setFailure(null)
          }}
        />
        <p className="text-right text-xs text-muted-foreground tabular-nums">
          {note.length} / {MAX_NOTE_LENGTH}
        </p>
      </div>

      <p className="text-xs text-muted-foreground">
        Пустое поле очистит заметку. Клиент её не увидит.
      </p>

      {failure && (
        <p role="alert" className="text-sm text-destructive">
          {failure.message}
        </p>
      )}

      <DialogFooter className="flex-col sm:flex-row">
        <Button
          type="button"
          variant="outline"
          className="h-11 sm:h-8"
          onClick={onClose}
          disabled={isPending}
        >
          Отмена
        </Button>
        <Button
          type="button"
          className="h-11 sm:h-8"
          onClick={() => void handleSave()}
          disabled={isPending || isTooLong}
          aria-busy={isPending}
        >
          {isPending && (
            <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
          )}
          {isPending ? 'Сохраняем…' : 'Сохранить'}
        </Button>
      </DialogFooter>
    </>
  )
}
