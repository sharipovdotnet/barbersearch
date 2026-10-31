import { useCallback, useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { CabinetBookingList } from '@/components/barber/cabinet-booking-list'
import type { RowHandle } from '@/components/barber/cabinet-booking-row'
import {
  BarberBookingActionDialog,
  type BarberActionKind,
  type BarberActionTarget,
} from '@/components/barber/barber-booking-action-dialog'
import {
  BarberCabinetHeading,
  BarberCabinetSkeleton,
  BarberProfileMissingGate,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import { BarberNoteDialog } from '@/components/barber/barber-note-dialog'
import { SessionUnavailable } from '@/components/session-unavailable'
import { useBarberBookings } from '@/hooks/use-barber-bookings'
import { useNow } from '@/hooks/use-now'
import { useSession } from '@/hooks/use-session'
import { BARBER_CABINET_PATH } from '@/lib/barber-cabinet-path'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'

/**
 * Кабинет барбера.
 *
 * Экран отвечает на один вопрос — «что у меня с записями клиентов» — и не пытается
 * быть расписанием: две вкладки, строки и пять действий. Список монтируется только
 * после проверок сессии, роли и привязанного профиля, поэтому лишних `403` нет.
 */
export function BarberCabinetPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Кабинет барбера — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            Загружаем кабинет…
          </p>
          <BarberCabinetSkeleton />
        </div>
      )}

      {status === 'unavailable' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SessionUnavailable />
        </div>
      )}

      {status === 'anonymous' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SignInGate
            title="Войдите, чтобы увидеть записи клиентов"
            description="Здесь барбер подтверждает записи, завершает визиты и оставляет заметки. Вход по email и паролю."
            redirectTo={BARBER_CABINET_PATH}
          />
        </div>
      )}

      {status === 'authenticated' && user && user.role !== 'Barber' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <BarberRoleGate role={user.role} />
        </div>
      )}

      {status === 'authenticated' && user && user.role === 'Barber' && (
        user.barberProfileId === null ? (
          <div className="flex min-h-[55vh] items-center justify-center py-6">
            <BarberProfileMissingGate />
          </div>
        ) : (
          <BarberBookingsScreen />
        )
      )}
    </div>
  )
}

type PendingFocus = {
  /** `null` — фокусировать заголовок списка. */
  id: string | null
  target: 'primary' | 'afterConfirm' | 'note'
}

/**
 * Экран списка монтируется только после проверок: аноним не должен сходить за
 * записями и получить `401` вместо гейта.
 */
function BarberBookingsScreen() {
  const [scope, setScope] = useState<BarberBookingScope>('upcoming')
  const bookings = useBarberBookings(scope)
  const now = useNow()
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [noteTarget, setNoteTarget] = useState<BarberBooking | null>(null)
  const [actionTarget, setActionTarget] = useState<BarberActionTarget | null>(
    null
  )
  // Сколько строк показала история: до первого её открытия предлагать нечего.
  const [knownPastCount, setKnownPastCount] = useState<number | null>(null)

  const headingRef = useRef<HTMLHeadingElement>(null)
  const rowHandles = useRef(new Map<string, RowHandle>())
  // Куда вернуть фокус после мутации: id строки и её кнопка.
  const pendingFocus = useRef<PendingFocus | null>(null)

  const registerRow = useCallback((id: string, handle: RowHandle | null) => {
    if (handle) {
      rowHandles.current.set(id, handle)
    } else {
      rowHandles.current.delete(id)
    }
  }, [])

  useEffect(() => {
    if (scope !== 'past' || bookings.status !== 'ready') {
      return
    }

    // oxlint-disable-next-line react/set-state-in-effect -- история нужна пустым «Ближайшим»
    setKnownPastCount(bookings.items.length)
  }, [scope, bookings.status, bookings.items.length])

  useEffect(() => {
    const pending = pendingFocus.current

    if (!pending) {
      return
    }

    pendingFocus.current = null

    // Диалог закрывается следующим коммитом, и Base UI возвращает фокус на элемент,
    // который был активен до открытия. Свой фокус ставим после кадра закрытия, иначе
    // он проигрывает этому возврату (например, после сохранения заметки).
    window.requestAnimationFrame(() => {
      if (pending.id !== null) {
        const handle = rowHandles.current.get(pending.id)

        if (handle) {
          if (pending.target === 'afterConfirm') {
            handle.focusAfterConfirm()
          } else if (pending.target === 'note') {
            handle.focusNote()
          } else {
            handle.focusPrimary()
          }

          return
        }
      }

      headingRef.current?.focus()
    })
  }, [bookings.items])

  /** Следующая строка после удаляемой: до мутации список ещё содержит её. */
  function nextRowId(id: string): string | null {
    const index = bookings.items.findIndex((booking) => booking.id === id)

    return index >= 0 ? (bookings.items[index + 1]?.id ?? null) : null
  }

  function finishTerminal(nextId: string | null, message: string) {
    pendingFocus.current = { id: nextId, target: 'primary' }
    setAnnouncement(message)

    // Строка была последней на странице, но сервер знает продолжение: иначе экран
    // соврал бы «записей нет».
    if (
      scope === 'upcoming' &&
      bookings.items.length <= 1 &&
      bookings.nextCursor !== null
    ) {
      bookings.loadMore()
    }
  }

  async function handleConfirm(id: string) {
    await bookings.confirm(id)
    pendingFocus.current = { id, target: 'afterConfirm' }
    setAnnouncement('Запись подтверждена.')
  }

  async function handleComplete(id: string) {
    const nextId = nextRowId(id)

    await bookings.complete(id)
    finishTerminal(nextId, 'Визит завершён.')
  }

  async function handleActionConfirm(
    kind: BarberActionKind,
    id: string
  ): Promise<void> {
    if (kind === 'cancel') {
      await bookings.cancel(id)
      return
    }

    await bookings.markNoShow(id)
  }

  function handleActionDone(
    kind: BarberActionKind,
    booking: BarberBooking
  ) {
    const nextId = nextRowId(booking.id)

    finishTerminal(
      nextId,
      kind === 'cancel' ? 'Запись отменена.' : 'Отмечена неявка.'
    )
  }

  async function handleSaveNote(id: string, note: string) {
    await bookings.saveNote(id, note)
    pendingFocus.current = { id, target: 'note' }
    setAnnouncement('Заметка сохранена.')
  }

  const hasPastItems =
    scope === 'past' ? bookings.items.length > 0 : (knownPastCount ?? 0) > 0

  return (
    <div className="space-y-5">
      <BarberCabinetHeading />

      {announcement && (
        <div
          role="status"
          className="flex items-start justify-between gap-3 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p>{announcement}</p>
          <button
            type="button"
            aria-label="Скрыть сообщение"
            className="rounded-md p-0.5 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={() => setAnnouncement(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      {bookings.requiresSignIn ? (
        <div className="flex min-h-[45vh] items-center justify-center py-6">
          <SignInGate
            title="Сессия истекла"
            description="Войдите снова, чтобы увидеть записи клиентов: cookie-сессия закончилась, пока страница была открыта."
            redirectTo={BARBER_CABINET_PATH}
            onSignedIn={bookings.reload}
          />
        </div>
      ) : (
        <CabinetBookingList
          scope={scope}
          items={bookings.items}
          status={bookings.status}
          error={bookings.error}
          nextCursor={bookings.nextCursor}
          isLoadingMore={bookings.isLoadingMore}
          loadMoreError={bookings.loadMoreError}
          now={now}
          hasPastItems={hasPastItems}
          headingRef={headingRef}
          onScopeChange={(next) => {
            setScope(next)
            setAnnouncement(null)
          }}
          onRetry={bookings.reload}
          onLoadMore={bookings.loadMore}
          onReload={bookings.reload}
          onConfirm={handleConfirm}
          onComplete={handleComplete}
          onOpenCancel={(booking) =>
            setActionTarget({ booking, kind: 'cancel' })
          }
          onOpenNoShow={(booking) =>
            setActionTarget({ booking, kind: 'noShow' })
          }
          onOpenNote={setNoteTarget}
          registerRow={registerRow}
        />
      )}

      <BarberNoteDialog
        booking={noteTarget}
        onClose={() => setNoteTarget(null)}
        onSave={handleSaveNote}
      />

      <BarberBookingActionDialog
        target={actionTarget}
        onClose={() => setActionTarget(null)}
        onConfirm={handleActionConfirm}
        onDone={handleActionDone}
        onReload={bookings.reload}
      />
    </div>
  )
}
