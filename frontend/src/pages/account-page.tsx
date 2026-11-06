import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { X } from 'lucide-react'

import {
  AccountAccess,
  BookingsErrorState,
  SignInGate,
} from '@/components/account/account-gates'
import { AccountShell } from '@/components/account/account-shell'
import { BarberRequestCard } from '@/components/account/barber-request-card'
import { BookingList, type BookingsTab } from '@/components/account/booking-list'
import { buildBookingView, type BookingView } from '@/components/account/booking-view'
import { CancelBookingDialog } from '@/components/account/cancel-booking-dialog'
import { NextBookingCard } from '@/components/account/next-booking-card'
import { ReviewDialog } from '@/components/account/review-dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { useAutoRefresh } from '@/hooks/use-auto-refresh'
import { useBarberProfiles } from '@/hooks/use-barber-profiles'
import { useCustomerBookings } from '@/hooks/use-bookings'
import { ACCOUNT_PATH } from '@/lib/account-path'
import { describeCustomerBookingsChange } from '@/lib/booking-changes'
import type { Booking } from '@/lib/bookings'
import { formatDateTime } from '@/lib/format'
import { formatPageRange } from '@/lib/paging'

/**
 * Кабинет: «Мои записи».
 *
 * Страница отвечает на один вопрос — «что у меня с визитами» — и не пытается быть
 * дашбордом: сверху ближайшая запись, ниже «Предстоящие» и «История». Вкладки — два
 * независимых серверных списка: скоуп (`upcoming`/`past`) определяет сервер, клиент
 * только переключает вкладку и листает страницы.
 *
 * Кабинет один для всех ролей: барбер и администратор тоже могут записаться к мастеру,
 * а их рабочие зоны открываются из каркаса отдельной ссылкой.
 */
export function AccountPage() {
  useEffect(() => {
    document.title = 'Мои записи — BarberSearch'
  }, [])

  return (
    <AccountAccess
      title="Войдите, чтобы увидеть записи"
      description="Здесь собраны ваши визиты: время, статус и отмена. Вход по email и паролю."
    >
      {(user) => (
        <AccountShell
          user={user}
          section="bookings"
          title="Мои записи"
          description="Ближайший визит, история и отмена — без звонков в салон."
        >
          <BookingsCabinet role={user.role} />
        </AccountShell>
      )}
    </AccountAccess>
  )
}

function BookingsCabinet({ role }: { role: string }) {
  const [tab, setTab] = useState<BookingsTab>('upcoming')
  // История ленивая: на входе в кабинет грузятся только «Предстоящие».
  const historyEnabled = tab === 'history'
  const { upcoming, history, cancelBooking, refreshUpcoming, isCancelling } =
    useCustomerBookings({
      historyEnabled,
    })

  // Активный список задаёт и строки, и пагинатор: у вкладок разные курсоры.
  const list = tab === 'history' ? history : upcoming
  const heroBooking = upcoming.items[0] ?? null

  const { profiles, failed: profilesFailed } = useBarberProfiles([
    ...upcoming.items.map((booking) => booking.barberProfileId),
    ...history.items.map((booking) => booking.barberProfileId),
    ...(heroBooking ? [heroBooking.barberProfileId] : []),
  ])

  const [now, setNow] = useState(() => Date.now())
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [cancelTarget, setCancelTarget] = useState<BookingView | null>(null)
  const [reviewTarget, setReviewTarget] = useState<BookingView | null>(null)
  const [reviewedIds, setReviewedIds] = useState<ReadonlySet<string>>(
    () => new Set()
  )

  const panelRef = useRef<HTMLDivElement>(null)

  // «через 2 часа» и «сегодня» устаревают молча: пересчитываем раз в минуту,
  // а не на каждый рендер.
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000)
    return () => window.clearInterval(timer)
  }, [])

  // Своя карточка на каждой строке: имя и адрес приходят из профиля барбера,
  // а не из брони, поэтому вид собирается после профилей.
  const hero = useMemo(
    () =>
      heroBooking
        ? buildBookingView(heroBooking, profiles[heroBooking.barberProfileId])
        : null,
    [heroBooking, profiles]
  )

  const listViews = useMemo(
    () =>
      list.items.map((booking) =>
        buildBookingView(booking, profiles[booking.barberProfileId])
      ),
    [list.items, profiles]
  )

  // Пояс салона живёт в карточке барбера, а не в брони: для баннера его собираем
  // тем же способом, что и строку списка.
  const timeZoneOf = useCallback(
    (booking: Booking) =>
      buildBookingView(booking, profiles[booking.barberProfileId]).timeZoneId,
    [profiles]
  )

  const handleUpcomingRefresh = useCallback(async () => {
    const change = await refreshUpcoming()

    if (change === null) {
      return
    }

    const message = describeCustomerBookingsChange(change, Date.now(), timeZoneOf)

    if (message !== null) {
      setAnnouncement(message)
    }
  }, [refreshUpcoming, timeZoneOf])

  const refreshHistory = history.refresh

  // «Обновить» перечитывает показанный список; опрос — только «Предстоящие»:
  // история — лог прошедших визитов, ему хватает загрузки при открытии вкладки.
  const handleManualRefresh = useCallback(() => {
    if (tab === 'upcoming') {
      void handleUpcomingRefresh()
      return
    }

    refreshHistory()
  }, [tab, handleUpcomingRefresh, refreshHistory])

  // Пока открыт диалог отмены или идёт мутация, список не трогаем: ответ,
  // прочитанный до коммита отмены, вернул бы строку обратно.
  useAutoRefresh({
    enabled:
      upcoming.status === 'ready' &&
      !upcoming.requiresSignIn &&
      cancelTarget === null &&
      !isCancelling,
    onRefresh: handleUpcomingRefresh,
  })

  function handleCancelled(view: BookingView) {
    setAnnouncement(
      `Запись на ${formatDateTime(view.booking.startAt, view.timeZoneId)} отменена.`
    )
  }

  function handleReviewed(view: BookingView) {
    setReviewedIds((current) => new Set(current).add(view.booking.id))
    setAnnouncement(
      'Спасибо! Отзыв отправлен на проверку: он появится на карточке барбера после модерации.'
    )
  }

  function handleSignedIn() {
    upcoming.reload()
    list.reload()
  }

  const requiresSignIn = upcoming.requiresSignIn || list.requiresSignIn
  const isHeroLoading = upcoming.status === 'loading' || upcoming.status === 'idle'
  const isListLoading = list.status === 'loading' || list.status === 'idle'

  return (
    <div className="space-y-5">
      {/* Карточка заявки — только клиенту: барберу и администратору она не нужна.
          У неё своя загрузка и своя ошибка, поэтому список записей она не блокирует. */}
      {role === 'Customer' && <BarberRequestCard />}

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

      {requiresSignIn ? (
        <div className="flex min-h-[45vh] items-center justify-center py-6">
          <SignInGate
            title="Сессия истекла"
            description="Войдите снова, чтобы увидеть свои записи: cookie-сессия закончилась, пока страница была открыта."
            redirectTo={ACCOUNT_PATH}
            onSignedIn={handleSignedIn}
          />
        </div>
      ) : (
        <div className="space-y-6">
          {isHeroLoading && <Skeleton className="h-64 rounded-xl" />}

          {upcoming.status === 'error' && (
            <BookingsErrorState
              message={upcoming.error ?? 'Не удалось загрузить записи.'}
              onRetry={upcoming.reload}
            />
          )}

          {upcoming.status === 'ready' && hero && (
            <NextBookingCard
              view={hero}
              now={now}
              onRequestCancel={() => setCancelTarget(hero)}
            />
          )}

          {isListLoading && <ListSkeleton />}

          {list.status === 'error' && (
            <BookingsErrorState
              message={list.error ?? 'Не удалось загрузить записи.'}
              onRetry={list.reload}
            />
          )}

          {list.status === 'ready' && (
            <BookingList
              items={listViews}
              tab={tab}
              onTabChange={setTab}
              reviewedIds={reviewedIds}
              rangeLabel={formatPageRange(list.offset, list.items.length)}
              hasPrev={list.hasPrev}
              hasNext={list.hasNext}
              isLoadingPage={list.isLoadingPage}
              pageError={list.pageError}
              onPrev={list.goPrev}
              onNext={list.goNext}
              pageIndex={list.pageIndex}
              focusTargetRef={panelRef}
              updatedAt={list.updatedAt}
              isRefreshing={list.isRefreshing}
              refreshError={list.refreshError}
              onRefresh={handleManualRefresh}
              onRequestCancel={setCancelTarget}
              onRequestReview={setReviewTarget}
            />
          )}

          {profilesFailed && (
            <p className="text-xs text-muted-foreground">
              Часть карточек барберов не загрузилась — имена и адреса могут быть
              неполными.
            </p>
          )}
        </div>
      )}

      <CancelBookingDialog
        view={cancelTarget}
        onClose={() => setCancelTarget(null)}
        onConfirm={cancelBooking}
        onCancelled={handleCancelled}
      />

      <ReviewDialog
        view={reviewTarget}
        onClose={() => setReviewTarget(null)}
        onSubmitted={handleReviewed}
      />
    </div>
  )
}

/** Скелетон активной вкладки: строки списка, а не всей страницы. */
function ListSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Загружаем записи…
      </p>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-8 w-64 rounded-lg" />
        <Skeleton className="h-52 rounded-xl" />
      </div>
    </>
  )
}
