import { useCallback, useEffect, useRef, useState, type MouseEvent } from 'react'
import { X } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import { BarberBookingDialog } from '@/components/barber/barber-booking-dialog'
import { BarberCabinetNav } from '@/components/barber/barber-cabinet-nav'
import { BarberCabinetViewTabs } from '@/components/barber/barber-cabinet-view-tabs'
import { BarberDay } from '@/components/barber/barber-day'
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
import { BookingPriceDialog } from '@/components/booking-price-dialog'
import { RescheduleBookingDialog } from '@/components/reschedule-booking-dialog'
import { RefreshStatus } from '@/components/refresh-status'
import { SessionUnavailable } from '@/components/session-unavailable'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { useBarberBookings } from '@/hooks/use-barber-bookings'
import { useBarberDay } from '@/hooks/use-barber-day'
import { useNow } from '@/hooks/use-now'
import { useSession } from '@/hooks/use-session'
import { useI18n } from '@/i18n'
import type { BarberSlot } from '@/lib/barber-slots'
import { localDateKey, type DayGap } from '@/lib/barber-day'
import {
  BARBER_CABINET_PANEL_ID,
  barberViewTabId,
  type BarberCabinetView,
} from '@/lib/barber-cabinet-view'
import { BARBER_CABINET_PATH, BARBER_IMPORT_PATH } from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'
import type {
  BarberBooking,
  BarberBookingScope,
} from '@/lib/barber-bookings'
import { describeBarberBookingsChange } from '@/lib/booking-changes'
import { describeBarberBookingFailure } from '@/lib/errors'
import { formatDateTime } from '@/lib/format'

/**
 * Кабинет барбера.
 *
 * Экран отвечает на один вопрос — «кто придёт сегодня и что у меня в ближайший
 * час» — и не пытается быть расписанием: три вкладки, строки и пять действий.
 * «Сегодня» собирается из первых страниц двух существующих списков (§3.2), поэтому
 * отдельного эндпоинта и второго контроллера пагинации здесь нет: переключение
 * «Сегодня → Ближайшие» не делает ни одного запроса.
 *
 * Список монтируется только после проверок сессии, роли и привязанного профиля,
 * поэтому лишних `403` нет.
 */
export function BarberCabinetPage() {
  const { status, user } = useSession()
  const { t, locale } = useI18n()

  useEffect(() => {
    document.title = t('Кабинет барбера — BarberSearch')
  }, [t, locale])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            {t('Загружаем кабинет…')}
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
            title={t('Войдите, чтобы увидеть записи клиентов')}
            description={t(
              'Здесь барбер подтверждает записи, завершает визиты и оставляет заметки. Вход по email и паролю.'
            )}
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
  const { t } = useI18n()
  const [view, setView] = useState<BarberCabinetView>('day')
  // Оба контроллера живут, пока открыт кабинет: два `GET` при монтировании и ни
  // одного при переключении вкладок.
  const upcoming = useBarberBookings('upcoming')
  const past = useBarberBookings('past')
  const day = useBarberDay(upcoming, past)
  const now = useNow()
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [noteTarget, setNoteTarget] = useState<BarberBooking | null>(null)
  const [rescheduleTarget, setRescheduleTarget] =
    useState<BarberBooking | null>(null)
  // Правка цены: цель, ожидание ответа и его ошибка живут на экране, потому что
  // диалог общий с карточкой администратора и маппер ошибок у кабинетов свой.
  const [priceTarget, setPriceTarget] = useState<BarberBooking | null>(null)
  const [isPricePending, setIsPricePending] = useState(false)
  const [priceError, setPriceError] = useState<string | null>(null)
  const [actionTarget, setActionTarget] = useState<BarberActionTarget | null>(
    null
  )
  // Диалог записи управляется страницей: окно дня открывает его с готовым временем,
  // а кнопка в шапке — с пустой формой.
  const [dialogOpen, setDialogOpen] = useState(false)
  const [dialogDate, setDialogDate] = useState<string | undefined>(undefined)
  const [dialogStartAt, setDialogStartAt] = useState<string | undefined>(
    undefined
  )

  const headingRef = useRef<HTMLHeadingElement>(null)
  const rowHandles = useRef(new Map<string, RowHandle>())
  // Куда вернуть фокус после мутации: id строки и её кнопка.
  const pendingFocus = useRef<PendingFocus | null>(null)

  const list = view === 'upcoming' ? upcoming : past
  const isDay = view === 'day'
  // Действия идут в тот контроллер, который сейчас на экране: у дня он прячет
  // маршрутизацию по id и перечитывание второго списка.
  const actions = isDay ? day : list
  const items = isDay ? day.items : list.items
  const isReady = isDay ? day.status === 'ready' : list.status === 'ready'
  const requiresSignIn = isDay
    ? day.requiresSignIn
    : list.requiresSignIn
  const isMutating = upcoming.isMutating || past.isMutating

  const refreshActive = isDay ? day.refresh : list.refresh
  const isRefreshing = isDay ? day.isRefreshing : list.isRefreshing
  const refreshError = isDay ? day.refreshError : list.refreshError
  const updatedAt = isDay ? day.updatedAt : list.updatedAt

  // Тихое обновление текущей вкладки. Баннер — только там, где новая запись
  // действительно новость: в истории появившаяся строка — завершённый визит.
  const handleRefresh = useCallback(async () => {
    const change = await refreshActive()

    if (change === null || view === 'past') {
      return
    }

    const message = describeBarberBookingsChange(change, Date.now())

    if (message !== null) {
      setAnnouncement(message)
    }
  }, [refreshActive, view])

  // Пока открыт диалог действия/переноса или идёт мутация, список не трогаем: строка
  // под руками не должна менять статус из-за ответа, прочитанного до коммита.
  useAutoRefresh({
    enabled:
      view !== 'past' &&
      isReady &&
      !requiresSignIn &&
      noteTarget === null &&
      actionTarget === null &&
      rescheduleTarget === null &&
      priceTarget === null &&
      !isMutating,
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
  }, [items])

  /** Следующая строка после удаляемой: до мутации список ещё содержит её. */
  function nextRowId(id: string): string | null {
    const index = items.findIndex((booking) => booking.id === id)

    return index >= 0 ? (items[index + 1]?.id ?? null) : null
  }

  function finishTerminal(nextId: string | null, message: string) {
    // Пустую страницу после действия обрабатывает общий контроллер страниц.
    pendingFocus.current = { id: nextId, target: 'primary' }
    setAnnouncement(message)
  }

  async function handleConfirm(id: string) {
    await actions.confirm(id)
    pendingFocus.current = { id, target: 'afterConfirm' }
    setAnnouncement(t('Запись подтверждена.'))
  }

  async function handleComplete(id: string) {
    const nextId = nextRowId(id)

    await actions.complete(id)
    finishTerminal(nextId, t('Визит завершён.'))
  }

  async function handleActionConfirm(
    kind: BarberActionKind,
    id: string,
    reason?: string
  ): Promise<void> {
    if (kind === 'cancel') {
      await actions.cancel(id, reason ?? '')
      return
    }

    await actions.markNoShow(id)
  }

  function handleActionDone(
    kind: BarberActionKind,
    booking: BarberBooking
  ) {
    const nextId = nextRowId(booking.id)

    finishTerminal(
      nextId,
      kind === 'cancel' ? t('Запись отменена.') : t('Отмечена неявка.')
    )
  }

  async function handleSaveNote(id: string, note: string) {
    await actions.saveNote(id, note)
    pendingFocus.current = { id, target: 'note' }
    setAnnouncement(t('Заметка сохранена.'))
  }

  /**
   * Перенос выполняет контроллер-владелец строки; второй список не перечитывается.
   * Объявление строится по времени слота — сервер ответил `204` без тела.
   */
  async function handleRescheduled(slot: BarberSlot) {
    if (rescheduleTarget === null) {
      return
    }

    await actions.reschedule(rescheduleTarget.id, slot)
    setAnnouncement(
      t('Визит перенесён на {when}.', {
        when: formatDateTime(slot.startAt, rescheduleTarget.salon.timeZoneId),
      })
    )
  }

  /**
   * Правка цены визита: общая ручка двух кабинетов, строка правится контроллером
   * локально. Собственное действие баннера не даёт — баннер остаётся чужой правке.
   */
  function openPrice(booking: BarberBooking) {
    setPriceError(null)
    setPriceTarget(booking)
  }

  function closePrice() {
    if (isPricePending) {
      return
    }

    setPriceTarget(null)
    setPriceError(null)
  }

  async function handlePriceSubmit(price: number) {
    if (priceTarget === null || isPricePending) {
      return
    }

    setIsPricePending(true)
    setPriceError(null)

    try {
      await actions.reprice(priceTarget.id, price)
      setPriceTarget(null)
      setAnnouncement(t('Цена визита обновлена.'))
    } catch (cause) {
      setPriceError(
        describeBarberBookingFailure(
          cause,
          t('Не удалось изменить цену визита.')
        ).message
      )
    } finally {
      setIsPricePending(false)
    }
  }

  /**
   * Новая запись всегда попадает в «Ближайшие»: если барбер смотрел историю,
   * возвращаемся на «Сегодня» — это вид по умолчанию, и строка сразу на месте.
   */
  function handleClientCreated(clientName: string) {
    setAnnouncement(t('Клиент записан: {name}.', { name: clientName }))

    if (view === 'day') {
      day.reload()
      return
    }

    if (view === 'past') {
      setView('day')
    }

    upcoming.reload()
  }

  /** Окно дня: диалог открывается с датой дня и началом окна. */
  function handleFillGap(gap: DayGap) {
    setDialogDate(localDateKey(now))
    setDialogStartAt(gap.startAt)
    setDialogOpen(true)
  }

  /** Пустой день: тот же диалог, но без желаемого времени. */
  function handleCreateFromEmpty() {
    setDialogDate(localDateKey(now))
    setDialogStartAt(undefined)
    setDialogOpen(true)
  }

  function handleDialogOpenChange(next: boolean) {
    setDialogOpen(next)

    // Закрытие забывает «желаемое время»: кнопка в шапке открывает пустую форму.
    if (!next) {
      setDialogDate(undefined)
      setDialogStartAt(undefined)
    }
  }

  function handleViewChange(next: BarberCabinetView) {
    // День собирается из первых страниц: страница, оставленная в соседней вкладке,
    // иначе молча потеряла бы начало списка. Запроса нет — данные уже в кэше.
    if (next === 'day') {
      upcoming.goFirst()
      past.goFirst()
    }

    setView(next)
    setAnnouncement(null)
  }

  const listScope: BarberBookingScope =
    view === 'past' ? 'past' : 'upcoming'

  return (
    <div className="space-y-5">
      <BarberCabinetHeading />
      <BarberCabinetNav active="bookings" />

      <div className="flex flex-col gap-2 rounded-xl border bg-card px-3 py-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-muted-foreground">
          {t(
            'Клиент звонит или приходит в салон? Запишите его сами — запись сразу подтверждена.'
          )}
        </p>
        <div className="flex flex-wrap items-center gap-2">
          <BarberBookingDialog
            barberProfileId={barberProfileId}
            initialDate={dialogDate}
            preferredStartAt={dialogStartAt}
            open={dialogOpen}
            onOpenChange={handleDialogOpenChange}
            onCreated={handleClientCreated}
          />
          {/* Старый журнал барбера: перенос одним вечером (§9.5). */}
          <a
            href={BARBER_IMPORT_PATH}
            className="inline-flex h-11 items-center justify-center rounded-lg border border-border bg-background px-2.5 text-sm font-medium whitespace-nowrap transition-colors hover:bg-muted focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none sm:h-8"
            onClick={(event: MouseEvent<HTMLAnchorElement>) => {
              if (isPlainClick(event)) {
                event.preventDefault()
                navigate(BARBER_IMPORT_PATH)
              }
            }}
          >
            {t('Перенести записи')}
          </a>
        </div>
      </div>

      {announcement && (
        <div
          role="status"
          className="flex items-start justify-between gap-3 rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          <p>{announcement}</p>
          <button
            type="button"
            aria-label={t('Скрыть сообщение')}
            className="rounded-md p-0.5 text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            onClick={() => setAnnouncement(null)}
          >
            <X className="size-3.5" aria-hidden />
          </button>
        </div>
      )}

      {requiresSignIn ? (
        <div className="flex min-h-[45vh] items-center justify-center py-6">
          <SignInGate
            title={t('Сессия истекла')}
            description={t(
              'Войдите снова, чтобы увидеть записи клиентов: cookie-сессия закончилась, пока страница была открыта.'
            )}
            redirectTo={BARBER_CABINET_PATH}
            onSignedIn={actions.reload}
          />
        </div>
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <BarberCabinetViewTabs
              view={view}
              onChange={handleViewChange}
            />
            <RefreshStatus
              updatedAt={updatedAt}
              isRefreshing={isRefreshing}
              onRefresh={() => {
                void handleRefresh()
              }}
            />
          </div>

          {refreshError && (
            <p role="alert" className="text-sm text-destructive">
              {refreshError}
            </p>
          )}

          <div
            id={BARBER_CABINET_PANEL_ID}
            role="tabpanel"
            aria-labelledby={barberViewTabId(view)}
            aria-busy={
              isDay ? day.status === 'loading' : list.status === 'loading'
            }
          >
            {isDay ? (
              <BarberDay
                day={day}
                now={now}
                headingRef={headingRef}
                onFillGap={handleFillGap}
                onCreate={handleCreateFromEmpty}
                onConfirm={handleConfirm}
                onComplete={handleComplete}
                onOpenCancel={(booking) =>
                  setActionTarget({ booking, kind: 'cancel' })
                }
                onOpenNoShow={(booking) =>
                  setActionTarget({ booking, kind: 'noShow' })
                }
                onOpenNote={setNoteTarget}
                onOpenReschedule={setRescheduleTarget}
                onOpenPrice={openPrice}
                onReload={day.reload}
                registerRow={registerRow}
              />
            ) : (
              <CabinetBookingList
                view={listScope}
                items={list.items}
                status={list.status}
                error={list.error}
                pageIndex={list.pageIndex}
                offset={list.offset}
                hasPrev={list.hasPrev}
                hasNext={list.hasNext}
                isLoadingPage={list.isLoadingPage}
                pageError={list.pageError}
                now={now}
                hasPastItems={past.items.length > 0}
                headingRef={headingRef}
                onRetry={list.reload}
                onPrev={list.goPrev}
                onNext={list.goNext}
                onReload={list.reload}
                onOpenPast={() => handleViewChange('past')}
                onConfirm={handleConfirm}
                onComplete={handleComplete}
                onOpenCancel={(booking) =>
                  setActionTarget({ booking, kind: 'cancel' })
                }
                onOpenNoShow={(booking) =>
                  setActionTarget({ booking, kind: 'noShow' })
                }
                onOpenNote={setNoteTarget}
                onOpenReschedule={setRescheduleTarget}
                onOpenPrice={openPrice}
                registerRow={registerRow}
              />
            )}
          </div>
        </>
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
        onReload={actions.reload}
      />

      <RescheduleBookingDialog
        variant="barber"
        target={
          rescheduleTarget === null
            ? null
            : {
                bookingId: rescheduleTarget.id,
                startAt: rescheduleTarget.startAt,
                endAt: rescheduleTarget.endAt,
                timeZoneId: rescheduleTarget.salon.timeZoneId,
                serviceName: rescheduleTarget.service.name,
              }
        }
        onClose={() => setRescheduleTarget(null)}
        onRescheduled={handleRescheduled}
      />

      {priceTarget !== null && (
        <BookingPriceDialog
          description={`${formatDateTime(priceTarget.startAt, priceTarget.salon.timeZoneId)} · ${priceTarget.customer.displayName}`}
          currentPrice={priceTarget.price}
          isPending={isPricePending}
          error={priceError}
          onSubmit={(price) => {
            void handlePriceSubmit(price)
          }}
          onClose={closePrice}
        />
      )}
    </div>
  )
}
