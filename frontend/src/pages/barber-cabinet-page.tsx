import { useCallback, useEffect, useRef, useState } from 'react'
import { X } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { BarberBookingDialog } from '@/components/barber/barber-booking-dialog'
import { BarberCabinetNav } from '@/components/barber/barber-cabinet-nav'
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
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { useBarberBookings } from '@/hooks/use-barber-bookings'
import { useNow } from '@/hooks/use-now'
import { useSession } from '@/hooks/use-session'
import { BARBER_CABINET_PATH } from '@/lib/barber-cabinet-path'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'
import { describeBarberBookingsChange } from '@/lib/booking-changes'

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
          <BarberBookingsScreen barberProfileId={user.barberProfileId} />
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
function BarberBookingsScreen({
  barberProfileId,
}: {
  barberProfileId: string
}) {
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

  const refreshBookings = bookings.refresh
  const isRefreshing = bookings.isRefreshing
  const refreshError = bookings.refreshError
  const updatedAt = bookings.updatedAt

  // Тихое обновление страницы. Баннер — только в «Ближайших»: в истории
  // появившаяся строка не «новая запись», а завершённый визит.
  const handleRefresh = useCallback(async () => {
    const change = await refreshBookings()

    if (change === null || scope !== 'upcoming') {
      return
    }

    const message = describeBarberBookingsChange(change, Date.now())

    if (message !== null) {
      setAnnouncement(message)
    }
  }, [refreshBookings, scope])

  // Пока открыт диалог действия или идёт мутация, список не трогаем: строка
  // под руками не должна менять статус из-за ответа, прочитанного до коммита.
  useAutoRefresh({
    enabled:
      scope === 'upcoming' &&
      bookings.status === 'ready' &&
      !bookings.requiresSignIn &&
      noteTarget === null &&
      actionTarget === null &&
      !bookings.isMutating,
    onRefresh: handleRefresh,
  })

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
    // Пустую страницу после действия обрабатывает общий контроллер страниц.
    pendingFocus.current = { id: nextId, target: 'primary' }
    setAnnouncement(message)
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
    id: string,
    reason?: string
  ): Promise<void> {
    if (kind === 'cancel') {
      await bookings.cancel(id, reason ?? '')
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

  /**
   * Новая запись всегда попадает в «Ближайшие»: если барбер смотрел историю,
   * переключаем вкладку, иначе просто перечитываем первую страницу.
   */
  function handleClientCreated(clientName: string) {
    setAnnouncement(`Клиент записан: ${clientName}.`)

    if (scope === 'upcoming') {
      bookings.reload()
    } else {
      setScope('upcoming')
    }
  }

  const hasPastItems =
    scope === 'past' ? bookings.items.length > 0 : (knownPastCount ?? 0) > 0

  return (
    <div className="space-y-5">
      <BarberCabinetHeading />
      <BarberCabinetNav active="bookings" />

      <div className="flex flex-col gap-2 rounded-xl border bg-card px-3 py-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-muted-foreground">
          Клиент звонит или приходит в салон? Запишите его сами — запись сразу
          подтверждена.
        </p>
        <BarberBookingDialog
          barberProfileId={barberProfileId}
          onCreated={handleClientCreated}
        />
      </div>

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
          pageIndex={bookings.pageIndex}
          offset={bookings.offset}
          hasPrev={bookings.hasPrev}
          hasNext={bookings.hasNext}
          isLoadingPage={bookings.isLoadingPage}
          pageError={bookings.pageError}
          now={now}
          hasPastItems={hasPastItems}
          headingRef={headingRef}
          updatedAt={updatedAt}
          isRefreshing={isRefreshing}
          refreshError={refreshError}
          onRefresh={handleRefresh}
          onScopeChange={(next) => {
            setScope(next)
            setAnnouncement(null)
          }}
          onRetry={bookings.reload}
          onPrev={bookings.goPrev}
          onNext={bookings.goNext}
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
