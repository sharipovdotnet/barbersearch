import { useState, type ReactNode, type RefObject } from 'react'
import { ArrowLeft, Circle, Loader2, TriangleAlert } from 'lucide-react'

import { BookingStatusBadge } from '@/components/booking-status-badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Skeleton } from '@/components/ui/skeleton'
import type { AdminBookingDetailStatus } from '@/hooks/use-admin-booking'
import { useNow } from '@/hooks/use-now'
import {
  cancelAdminBooking,
  describeAdminBookingActionError,
  formatAdminBookingTime,
  getAdminBookingCustomerName,
  markAdminBookingNoShow,
  type AdminBooking,
} from '@/lib/admin-bookings'
import { ApiError } from '@/lib/api'
import { canCancelBooking } from '@/lib/booking-status'
import {
  formatDateTime,
  formatDuration,
  formatPrice,
  formatShortDateTime,
} from '@/lib/format'

export type BookingActionKind = 'cancel' | 'noShow'

/**
 * Карточка брони: контекст клиента, барбера, услуги и салона плюс две
 * существующие операции поддержки.
 *
 * Время показывается только в поясе салона: `startAt`/`endAt` приходят в UTC,
 * и подпись «Время в поясе салона» говорит об этом прямо. Новых операций нет:
 * «Отменить бронь» доступна `Pending`/`Confirmed`, «Отметить неявку» — только
 * `Confirmed` после начала визита (клиентская проверка для UX, сервер проверит
 * ещё раз). Обе необратимы и подтверждаются `alertdialog`'ом. Брони не
 * аудируются, поэтому экран не обещает запись в журнале.
 */
export function BookingDetail({
  booking,
  headingRef,
  onReload,
  onBack,
  onRequiresSignIn,
}: {
  booking: AdminBooking
  headingRef: RefObject<HTMLHeadingElement | null>
  /** Перечитать карточку и список: статус в строке списка уже устарел. */
  onReload: () => void
  onBack: () => void
  onRequiresSignIn: () => void
}) {
  const [target, setTarget] = useState<BookingActionKind | null>(null)
  const [pending, setPending] = useState<BookingActionKind | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [announcement, setAnnouncement] = useState<string | null>(null)

  const now = useNow()
  const durationMinutes = Math.round(
    (Date.parse(booking.endAt) - Date.parse(booking.startAt)) / 60_000
  )

  // Клиентская проверка для UX: сервер всё равно откажет в неявке раньше визита.
  const canMarkNoShow =
    booking.status === 'Confirmed' && Date.parse(booking.startAt) <= now
  const canCancel = canCancelBooking(booking.status)
  const hasActions = canCancel || canMarkNoShow
  const customerName = getAdminBookingCustomerName(booking.customer)

  function openAction(kind: BookingActionKind) {
    setActionError(null)
    setTarget(kind)
  }

  function closeAction() {
    if (pending !== null) {
      return
    }

    setTarget(null)
    setActionError(null)
  }

  async function confirmAction(kind: BookingActionKind) {
    if (pending !== null) {
      return
    }

    setPending(kind)
    setActionError(null)

    try {
      if (kind === 'cancel') {
        await cancelAdminBooking(booking.id)
      } else {
        await markAdminBookingNoShow(booking.id)
      }

      setTarget(null)
      setAnnouncement(
        kind === 'cancel' ? 'Бронь отменена.' : 'Неявка отмечена.'
      )
      onReload()

      // Кнопка-триггер исчезает вместе со старым статусом: фокус возвращаем
      // на заголовок карточки, а не оставляем в закрытом диалоге.
      window.requestAnimationFrame(() => headingRef.current?.focus())
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        onRequiresSignIn()
        return
      }

      setActionError(
        describeAdminBookingActionError(
          cause,
          kind === 'cancel'
            ? 'Не удалось отменить бронь. Попробуйте ещё раз.'
            : 'Не удалось отметить неявку. Попробуйте ещё раз.'
        )
      )
    } finally {
      setPending(null)
    }
  }

  return (
    <section
      id="booking-detail"
      aria-labelledby="booking-detail-title"
      aria-busy={pending !== null}
      className="overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10"
    >
      <div className="space-y-5 p-4 sm:p-5">
        <div className="flex items-center justify-between gap-3">
          <Button
            variant="ghost"
            size="sm"
            className="h-11 sm:h-7 lg:hidden"
            onClick={onBack}
          >
            <ArrowLeft data-icon="inline-start" aria-hidden />
            Назад
          </Button>
          <div className="ml-auto">
            <BookingStatusBadge status={booking.status} />
          </div>
        </div>

        <div className="min-w-0">
          <h2
            id="booking-detail-title"
            ref={headingRef}
            tabIndex={-1}
            className="scroll-mt-20 font-heading text-xl leading-tight font-semibold tracking-tight"
          >
            Бронь
          </h2>
          <p className="mt-0.5 text-sm break-words text-muted-foreground">
            {formatAdminBookingTime(booking)} · {customerName}
          </p>
        </div>

        {announcement && (
          <p
            role="status"
            className="rounded-lg border bg-muted/50 px-3 py-2 text-sm"
          >
            {announcement}
          </p>
        )}

        <dl className="grid gap-4 border-t pt-4 sm:grid-cols-2">
          <ContextItem label="Начало">
            <time dateTime={booking.startAt}>
              {formatAdminBookingTime(booking)}
            </time>
          </ContextItem>

          <ContextItem label="Окончание">
            <time dateTime={booking.endAt}>
              {formatShortDateTime(booking.endAt, booking.salon.timeZoneId)}
            </time>
          </ContextItem>

          <ContextItem label="Длительность">
            {formatDuration(durationMinutes) || '—'}
          </ContextItem>

          <ContextItem label="Цена">{formatPrice(booking.price)}</ContextItem>

          <ContextItem label="Клиент">
            <span className="block break-words">{customerName}</span>
            <span className="block break-all text-muted-foreground">
              {booking.customer.email}
            </span>
            {booking.customer.phoneNumber && (
              <span className="block text-muted-foreground tabular-nums">
                {booking.customer.phoneNumber}
              </span>
            )}
          </ContextItem>

          <ContextItem label="Барбер">
            <span className="break-words">{booking.barber.displayName}</span>
          </ContextItem>

          <ContextItem label="Услуга">
            <span className="break-words">{booking.service.name}</span>
          </ContextItem>

          <ContextItem label="Салон">
            <span className="block break-words">{booking.salon.name}</span>
            <span className="block break-words text-muted-foreground">
              {booking.salon.city}, {booking.salon.address}
            </span>
            <span className="mt-1 block text-xs text-muted-foreground">
              Время в поясе салона: {booking.salon.timeZoneId}
            </span>
          </ContextItem>

          <ContextItem label="Создана">
            {formatDateTime(booking.createdAt, booking.salon.timeZoneId)}
          </ContextItem>

          <ContextItem label="Номер брони">
            <span className="break-all">{booking.id}</span>
          </ContextItem>
        </dl>

        {(booking.customerNote || booking.barberNote) && (
          <section
            aria-labelledby="booking-notes-title"
            className="space-y-3 border-t pt-4"
          >
            <h3 id="booking-notes-title" className="text-sm font-medium">
              Заметки
            </h3>

            {booking.customerNote && (
              <Note label="Клиент" text={booking.customerNote} />
            )}
            {booking.barberNote && (
              <Note label="Барбер" text={booking.barberNote} />
            )}
          </section>
        )}

        {hasActions && (
          <section
            aria-labelledby="booking-actions-title"
            className="space-y-3 border-t pt-4"
          >
            <h3 id="booking-actions-title" className="text-sm font-medium">
              Действия
            </h3>

            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
              {canCancel && (
                <Button
                  variant="outline"
                  className="h-11 sm:h-9"
                  onClick={() => openAction('cancel')}
                >
                  Отменить бронь
                </Button>
              )}
              {canMarkNoShow && (
                <Button
                  variant="destructive"
                  className="h-11 sm:h-9"
                  onClick={() => openAction('noShow')}
                >
                  Отметить неявку
                </Button>
              )}
            </div>

            <p className="text-xs text-muted-foreground">
              Доступны только отмена и отметка неявки. Заметку барбера
              администратор не меняет.
            </p>
          </section>
        )}
      </div>

      {target !== null && (
        <BookingActionDialog
          kind={target}
          booking={booking}
          isPending={pending !== null}
          error={actionError}
          onClose={closeAction}
          onConfirm={() => {
            void confirmAction(target)
          }}
        />
      )}
    </section>
  )
}

/**
 * Подтверждение действия: `alertdialog`, потому что оба действия необратимы.
 *
 * Содержимое монтируется на каждое открытие вместе с диалогом, поэтому ошибка
 * прошлой брони не появляется над следующей.
 */
function BookingActionDialog({
  kind,
  booking,
  isPending,
  error,
  onClose,
  onConfirm,
}: {
  kind: BookingActionKind
  booking: AdminBooking
  isPending: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}) {
  const isCancel = kind === 'cancel'

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
      <DialogContent role="alertdialog" className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>
            {isCancel ? 'Отменить бронь?' : 'Отметить неявку?'}
          </DialogTitle>
          <DialogDescription>
            {formatAdminBookingTime(booking)} ·{' '}
            {getAdminBookingCustomerName(booking.customer)}
          </DialogDescription>
        </DialogHeader>

        <div className="flex gap-2.5 rounded-lg bg-destructive/10 p-3">
          <TriangleAlert
            className="mt-0.5 size-4 shrink-0 text-destructive"
            aria-hidden
          />
          <p className="text-sm text-foreground/80">
            {isCancel
              ? 'Бронь будет отменена. Клиент и барбер увидят новый статус.'
              : 'Визит будет отмечен как неявка. Отменить это действие нельзя.'}
          </p>
        </div>

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
            disabled={isPending}
            onClick={onClose}
          >
            Оставить как есть
          </Button>
          <Button
            type="button"
            variant="destructive"
            className="h-11 sm:h-8"
            disabled={isPending}
            aria-busy={isPending}
            onClick={onConfirm}
          >
            {isPending && (
              <Loader2
                data-icon="inline-start"
                aria-hidden
                className="animate-spin"
              />
            )}
            {isCancel ? 'Отменить бронь' : 'Отметить неявку'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Заглушка правой колонки, пока бронь не выбрана. */
export function BookingDetailPlaceholder() {
  return (
    <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <Circle className="size-4 text-muted-foreground" aria-hidden />
      </div>
      <div className="space-y-1">
        <p className="font-medium">Выберите бронь</p>
        <p className="mx-auto max-w-sm text-sm text-muted-foreground">
          Слева список: откройте строку, чтобы увидеть клиента, барбера и действия
          поддержки.
        </p>
      </div>
    </div>
  )
}

/** Состояние правой колонки: загрузка, ошибка или потерянная бронь. */
export function BookingDetailState({
  status,
  error,
  onRetry,
  onBack,
}: {
  status: AdminBookingDetailStatus
  error: string | null
  onRetry: () => void
  onBack: () => void
}) {
  if (status === 'notFound') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Circle className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Бронь не найдена.</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Возможно, ссылка устарела. Вернитесь к списку — он покажет актуальные
            брони.
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onBack}
        >
          К списку
        </Button>
      </div>
    )
  }

  if (status === 'error') {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center gap-3 rounded-xl border border-dashed px-6 py-10 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-destructive/10">
          <TriangleAlert className="size-4 text-destructive" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Не удалось загрузить бронь</p>
          <p
            role="alert"
            className="mx-auto max-w-sm text-sm text-muted-foreground"
          >
            {error ?? 'Попробуйте ещё раз.'}
          </p>
        </div>
        <Button
          size="sm"
          variant="outline"
          className="h-11 sm:h-7"
          onClick={onRetry}
        >
          Повторить
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-3" aria-busy="true">
      <p role="status" className="sr-only">
        Загружаем бронь…
      </p>
      <Skeleton className="h-12 w-40" aria-hidden />
      <Skeleton className="h-6 w-60" aria-hidden />
      <Skeleton className="h-64 rounded-xl" aria-hidden />
    </div>
  )
}

function Note({ label, text }: { label: string; text: string }) {
  return (
    <div className="rounded-lg border bg-muted/40 p-3">
      <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
        {label}
      </p>
      <p className="mt-1 text-sm break-words whitespace-pre-line">{text}</p>
    </div>
  )
}

function ContextItem({
  label,
  children,
}: {
  label: string
  children: ReactNode
}) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
        {label}
      </dt>
      <dd className="mt-1 text-sm">{children}</dd>
    </div>
  )
}
