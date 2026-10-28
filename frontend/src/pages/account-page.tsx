import { useEffect, useMemo, useState } from 'react'
import { Scissors, X } from 'lucide-react'

import { AccountSkeleton, BookingsErrorState, RoleGate, SignInGate } from '@/components/account/account-gates'
import { BookingList, type BookingsTab } from '@/components/account/booking-list'
import { buildBookingView, type BookingView } from '@/components/account/booking-view'
import { CancelBookingDialog } from '@/components/account/cancel-booking-dialog'
import { ChangePasswordDialog } from '@/components/account/change-password-dialog'
import { NextBookingCard } from '@/components/account/next-booking-card'
import { HowItWorksCard, ProfileCard } from '@/components/account/profile-card'
import { ReviewDialog } from '@/components/account/review-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useBarberProfiles } from '@/hooks/use-barber-profiles'
import { useBookings } from '@/hooks/use-bookings'
import { useSession } from '@/hooks/use-session'
import type { SessionUser } from '@/lib/auth'
import { isUpcomingBooking } from '@/lib/booking-status'
import { formatDateTime } from '@/lib/format'
import { goToSection, navigate } from '@/lib/router'

/**
 * Кабинет клиента.
 *
 * Страница отвечает на один вопрос — «что у меня с визитами» — и не пытается быть
 * дашбордом: сверху ближайшая запись, ниже предстоящие и история, сбоку аккаунт.
 * Клиент, барбер и администратор видят разные экраны: реализован только клиентский.
 */
export function AccountPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Мои записи — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && <AccountSkeleton />}

      {status === 'anonymous' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <SignInGate />
        </div>
      )}

      {status === 'authenticated' && user && user.role !== 'Customer' && (
        <div className="flex min-h-[55vh] items-center justify-center py-6">
          <RoleGate role={user.role} />
        </div>
      )}

      {status === 'authenticated' && user && user.role === 'Customer' && (
        <CustomerCabinet user={user} />
      )}
    </div>
  )
}

function CustomerCabinet({ user }: { user: SessionUser }) {
  const { signOut } = useSession()
  const bookings = useBookings()
  const { profiles, failed: profilesFailed } = useBarberProfiles(
    bookings.items.map((booking) => booking.barberProfileId)
  )

  const [now, setNow] = useState(() => Date.now())
  const [tab, setTab] = useState<BookingsTab>('upcoming')
  const [announcement, setAnnouncement] = useState<string | null>(null)
  const [cancelTarget, setCancelTarget] = useState<BookingView | null>(null)
  const [reviewTarget, setReviewTarget] = useState<BookingView | null>(null)
  const [reviewedIds, setReviewedIds] = useState<ReadonlySet<string>>(
    () => new Set()
  )
  const [isPasswordOpen, setIsPasswordOpen] = useState(false)

  // «через 2 часа» и «сегодня» устаревают молча: пересчитываем раз в минуту,
  // а не на каждый рендер.
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000)
    return () => window.clearInterval(timer)
  }, [])

  const views = useMemo(
    () =>
      bookings.items.map((booking) =>
        buildBookingView(booking, profiles[booking.barberProfileId])
      ),
    [bookings.items, profiles]
  )

  const { upcoming, history } = useMemo(() => {
    const next = views
      .filter((view) => isUpcomingBooking(view.booking, now))
      .sort((left, right) =>
        left.booking.startAt.localeCompare(right.booking.startAt)
      )
    const upcomingIds = new Set(next.map((view) => view.booking.id))
    const past = views
      .filter((view) => !upcomingIds.has(view.booking.id))
      .sort((left, right) =>
        right.booking.startAt.localeCompare(left.booking.startAt)
      )

    return { upcoming: next, history: past }
  }, [views, now])

  const hero = upcoming[0] ?? null

  async function handleSignOut() {
    await signOut()
    navigate('/')
  }

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

  return (
    <div className="space-y-5">
      <header>
        <h1 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
          Мои записи
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Ближайший визит, история и отмена — без звонков в салон.
        </p>
      </header>

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
            description="Войдите снова, чтобы увидеть свои записи: cookie-сессия закончилась, пока страница была открыта."
            onSignedIn={bookings.reload}
          />
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_19rem] lg:items-start">
          <div className="space-y-6">
            {(bookings.status === 'loading' || bookings.status === 'idle') && (
              <ContentSkeleton />
            )}

            {bookings.status === 'error' && (
              <BookingsErrorState
                message={bookings.error ?? 'Не удалось загрузить записи.'}
                onRetry={bookings.reload}
              />
            )}

            {bookings.status === 'ready' && (
              <>
                {hero ? (
                  <NextBookingCard
                    view={hero}
                    now={now}
                    onRequestCancel={() => setCancelTarget(hero)}
                  />
                ) : (
                  <NoUpcomingCard hasHistory={history.length > 0} />
                )}

                {bookings.items.length > 0 && (
                  <BookingList
                    upcoming={upcoming}
                    history={history}
                    tab={tab}
                    onTabChange={setTab}
                    reviewedIds={reviewedIds}
                    nextCursor={bookings.nextCursor}
                    isLoadingMore={bookings.isLoadingMore}
                    loadMoreError={bookings.loadMoreError}
                    onLoadMore={bookings.loadMore}
                    onRequestCancel={setCancelTarget}
                    onRequestReview={setReviewTarget}
                  />
                )}

                {profilesFailed && (
                  <p className="text-xs text-muted-foreground">
                    Часть карточек барберов не загрузилась — имена и адреса могут
                    быть неполными.
                  </p>
                )}
              </>
            )}
          </div>

          <aside className="space-y-4 lg:sticky lg:top-20">
            <ProfileCard
              user={user}
              onChangePassword={() => setIsPasswordOpen(true)}
              onSignOut={() => void handleSignOut()}
            />
            <HowItWorksCard />
          </aside>
        </div>
      )}

      <CancelBookingDialog
        view={cancelTarget}
        onClose={() => setCancelTarget(null)}
        onConfirm={bookings.cancelBooking}
        onCancelled={handleCancelled}
      />

      <ReviewDialog
        view={reviewTarget}
        onClose={() => setReviewTarget(null)}
        onSubmitted={handleReviewed}
      />

      <ChangePasswordDialog
        open={isPasswordOpen}
        onOpenChange={setIsPasswordOpen}
        onSuccess={() =>
          setAnnouncement(
            'Пароль изменён. Остальные сессии входа завершены — здесь вы остались.'
          )
        }
      />
    </div>
  )
}

/** Пустой кабинет: не белая дыра, а приглашение к первой записи. */
function NoUpcomingCard({ hasHistory }: { hasHistory: boolean }) {
  if (hasHistory) {
    return (
      <div className="flex flex-col items-center gap-3 rounded-xl border border-dashed px-6 py-8 text-center">
        <div className="flex size-10 items-center justify-center rounded-full bg-muted">
          <Scissors className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <p className="font-medium">Предстоящих визитов нет</p>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Все прошлые записи — в истории ниже. Можно выбрать барбера и записаться
            снова.
          </p>
        </div>
        <Button size="sm" onClick={() => goToSection('barbers')}>
          Найти барбера
        </Button>
      </div>
    )
  }

  return (
    <Card>
      <CardContent className="flex flex-col items-center gap-4 py-10 text-center">
        <div className="flex size-12 items-center justify-center rounded-full bg-muted">
          <Scissors className="size-5 text-muted-foreground" aria-hidden />
        </div>
        <div className="space-y-1">
          <h2 className="font-heading text-lg font-semibold tracking-tight">
            Пока ни одной записи
          </h2>
          <p className="mx-auto max-w-sm text-sm text-muted-foreground">
            Выберите барбера, услугу и свободное время — запись появится здесь,
            вместе со статусом и возможностью отмены.
          </p>
        </div>
        <p className="text-xs text-muted-foreground">
          Например: Fade, Beard, Classic cut
        </p>
        <Button onClick={() => goToSection('barbers')}>
          <Scissors data-icon="inline-start" aria-hidden />
          Найти барбера
        </Button>
      </CardContent>
    </Card>
  )
}

function ContentSkeleton() {
  return (
    <>
      <p role="status" className="sr-only">
        Загружаем записи…
      </p>
      <div className="space-y-6" aria-hidden>
        <Skeleton className="h-64 rounded-xl" />
        <div className="space-y-3">
          <Skeleton className="h-8 w-64 rounded-lg" />
          <Skeleton className="h-52 rounded-xl" />
        </div>
      </div>
    </>
  )
}
