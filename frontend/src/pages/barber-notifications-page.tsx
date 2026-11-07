import { useEffect, useRef, useState, type MouseEvent } from 'react'
import { ArrowLeft, ChevronRight, Loader2 } from 'lucide-react'

import { SignInGate } from '@/components/account/account-gates'
import {
  BarberCabinetSkeleton,
  BarberProfileMissingGate,
  BarberRoleGate,
} from '@/components/barber/barber-cabinet-gates'
import { ListPager } from '@/components/list-pager'
import { SessionUnavailable } from '@/components/session-unavailable'
import { Button, buttonVariants } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import {
  useBarberNotifications,
  type BarberNotificationsController,
} from '@/hooks/use-barber-notifications'
import {
  useNotificationSettings,
  type NotificationSettingsController,
} from '@/hooks/use-notification-settings'
import { useSession } from '@/hooks/use-session'
import {
  BARBER_CABINET_PATH,
  BARBER_NOTIFICATIONS_PATH,
} from '@/lib/barber-cabinet-path'
import {
  createTelegramLink,
  DEFAULT_LEAD_MINUTES,
  describeTelegramLinkError,
  describeTelegramUnlinkError,
  NOTIFICATION_LEAD_MINUTES,
  unlinkTelegram,
  type BarberNotification,
  type NotificationSettingsInput,
  type TelegramLink,
} from '@/lib/barber-notifications'
import {
  formatBookingsCount,
  formatShortDay,
  formatTime,
  relativeDayLabel,
} from '@/lib/format'
import { formatPageRange } from '@/lib/paging'
import { isPlainClick, navigate } from '@/lib/router'
import { cn } from '@/lib/utils'

/**
 * Инбокс напоминаний барбера: утренний дайджест и «через 30 минут».
 *
 * Экран — утилита кабинета, а не четвёртый раздел навигации (§8.1): вход — колокольчик
 * в заголовке, поэтому сам маршрут не появляется в `BarberCabinetNav`. Сверху
 * настройки (одна `PUT` на изменение с дебаунсом), ниже курсорный список: строка
 * открывает кабинет и отмечает себя прочитанной, «Прочитать все» гасит точки без
 * перезагрузки страницы.
 *
 * Гейты те же, что у остальных экранов кабинета: пока сессия, роль и профиль не
 * проверены, ни список, ни настройки не делают запросов.
 */
export function BarberNotificationsPage() {
  const { status, user } = useSession()

  useEffect(() => {
    document.title = 'Уведомления — BarberSearch'
  }, [])

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10">
      {status === 'loading' && (
        <div className="space-y-5">
          <p role="status" className="sr-only">
            Загружаем уведомления…
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
            title="Войдите, чтобы увидеть уведомления"
            description="Напоминания о записях и утренний дайджест доступны барберу после входа. Вход по email и паролю."
            redirectTo={BARBER_NOTIFICATIONS_PATH}
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
          <BarberNotificationsScreen key={user.barberProfileId} />
        )
      )}
    </div>
  )
}

function BarberNotificationsScreen() {
  const notifications = useBarberNotifications()
  const settings = useNotificationSettings()
  const [announcement, setAnnouncement] = useState<string | null>(null)

  const leadMinutes = settings.settings?.leadMinutes ?? DEFAULT_LEAD_MINUTES
  const canMarkAll =
    notifications.unreadCount > 0 && !notifications.isMarkingAllRead

  async function handleOpen(notification: BarberNotification) {
    // Отметка прочитанным — фон: строка открывается, даже если запрос не удался.
    await notifications.read(notification.id)
    navigate(BARBER_CABINET_PATH)
  }

  async function handleMarkAll() {
    setAnnouncement(null)

    if (await notifications.markAllRead()) {
      setAnnouncement('Все уведомления прочитаны.')
    }
  }

  return (
    <div className="space-y-5">
      <a
        href={BARBER_CABINET_PATH}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground underline-offset-2 hover:underline focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        onClick={(event: MouseEvent<HTMLAnchorElement>) => {
          if (isPlainClick(event)) {
            event.preventDefault()
            navigate(BARBER_CABINET_PATH)
          }
        }}
      >
        <ArrowLeft className="size-4" aria-hidden />
        Записи клиентов
      </a>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <h1 className="font-heading text-2xl font-semibold tracking-tight break-words sm:text-3xl">
          Уведомления
        </h1>
        <Button
          type="button"
          variant="outline"
          className="h-11 shrink-0 sm:h-8"
          disabled={!canMarkAll}
          aria-busy={notifications.isMarkingAllRead}
          onClick={() => {
            void handleMarkAll()
          }}
        >
          {notifications.isMarkingAllRead && (
            <Loader2 data-icon="inline-start" aria-hidden className="animate-spin" />
          )}
          Прочитать все
        </Button>
      </div>

      {announcement && (
        <p
          role="status"
          className="rounded-lg border bg-muted/50 px-3 py-2 text-sm"
        >
          {announcement}
        </p>
      )}

      {notifications.markAllError && (
        <p role="alert" className="text-sm text-destructive">
          {notifications.markAllError}
        </p>
      )}

      <NotificationSettingsSection controller={settings} />

      <NotificationsList
        controller={notifications}
        leadMinutes={leadMinutes}
        onOpen={handleOpen}
      />
    </div>
  )
}

function NotificationSettingsSection({
  controller,
}: {
  controller: NotificationSettingsController
}) {
  const {
    status,
    settings,
    loadError,
    saveError,
    fieldErrors,
    isSaving,
    update,
    reload,
  } = controller
  const [telegramLink, setTelegramLink] = useState<TelegramLink | null>(null)
  const [telegramError, setTelegramError] = useState<string | null>(null)
  const [isTelegramPending, setIsTelegramPending] = useState(false)

  function patch(patchValue: Partial<NotificationSettingsInput>) {
    update(patchValue)
  }

  async function handleLinkTelegram() {
    setTelegramError(null)
    setIsTelegramPending(true)

    try {
      setTelegramLink(await createTelegramLink())
    } catch (cause) {
      setTelegramError(describeTelegramLinkError(cause))
    } finally {
      setIsTelegramPending(false)
    }
  }

  async function handleUnlinkTelegram() {
    setTelegramError(null)
    setIsTelegramPending(true)

    try {
      await unlinkTelegram()
      setTelegramLink(null)
      reload()
    } catch (cause) {
      setTelegramError(describeTelegramUnlinkError(cause))
    } finally {
      setIsTelegramPending(false)
    }
  }

  function handleCheckTelegram() {
    setTelegramError(null)
    reload()
  }

  return (
    <section aria-labelledby="notifications-settings" className="space-y-3">
      <h2
        id="notifications-settings"
        className="font-heading text-lg font-semibold"
      >
        Настройки
      </h2>

      {status === 'loading' && (
        <div aria-busy="true" className="space-y-2">
          <span className="sr-only">Загружаем настройки…</span>
          <Skeleton className="h-24 w-full rounded-xl" aria-hidden />
        </div>
      )}

      {status === 'error' && (
        <div className="space-y-3 rounded-xl border bg-card p-4">
          <p role="alert" className="text-sm text-destructive">
            {loadError ?? 'Не удалось загрузить настройки.'}
          </p>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={reload}
          >
            Повторить
          </Button>
        </div>
      )}

      {status === 'ready' && settings && (
        <div className="space-y-4 rounded-xl border bg-card p-4">
          {loadError && (
            <p role="alert" className="text-sm text-destructive">
              {loadError}
            </p>
          )}

          <div className="flex items-center gap-3">
            <Checkbox
              id="notifications-enabled"
              checked={settings.isEnabled}
              onCheckedChange={(checked) => patch({ isEnabled: checked })}
            />
            <Label htmlFor="notifications-enabled">
              Присылать напоминания
            </Label>
          </div>

          <div className="flex flex-wrap items-start gap-x-3 gap-y-2">
            <div className="flex items-center gap-3">
              <Checkbox
                id="notifications-digest"
                checked={settings.digestEnabled}
                onCheckedChange={(checked) => patch({ digestEnabled: checked })}
              />
              <Label htmlFor="notifications-digest">Утренний дайджест</Label>
            </div>

            <div className="space-y-1">
              <Input
                id="notifications-digest-time"
                type="time"
                step={900}
                min="06:00"
                max="12:00"
                aria-label="Время утреннего дайджеста"
                value={settings.digestTime}
                aria-invalid={Boolean(fieldErrors.digestTime)}
                aria-describedby={
                  fieldErrors.digestTime
                    ? 'notifications-digest-time-error'
                    : undefined
                }
                onChange={(event) => patch({ digestTime: event.target.value })}
                className="h-11 w-32 sm:h-8"
              />
              {fieldErrors.digestTime && (
                <p
                  id="notifications-digest-time-error"
                  role="alert"
                  className="text-xs text-destructive"
                >
                  {fieldErrors.digestTime}
                </p>
              )}
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="notifications-lead">Напомнить за</Label>
            <select
              id="notifications-lead"
              value={settings.leadMinutes}
              aria-invalid={Boolean(fieldErrors.leadMinutes)}
              aria-describedby={
                fieldErrors.leadMinutes ? 'notifications-lead-error' : undefined
              }
              onChange={(event) => {
                const value = Number(event.target.value)

                if (value === 15 || value === 30 || value === 60) {
                  patch({ leadMinutes: value })
                }
              }}
              className="h-11 w-full max-w-48 rounded-lg border border-input bg-transparent px-2.5 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 sm:h-8"
            >
              {NOTIFICATION_LEAD_MINUTES.map((minutes) => (
                <option key={minutes} value={minutes}>
                  {minutes} минут
                </option>
              ))}
            </select>
            {fieldErrors.leadMinutes && (
              <p
                id="notifications-lead-error"
                role="alert"
                className="text-xs text-destructive"
              >
                {fieldErrors.leadMinutes}
              </p>
            )}
          </div>

          <div className="space-y-2 border-t pt-3">
            {!settings.botConfigured ? (
              <>
                <p className="text-sm text-muted-foreground">
                  Telegram не настроен. Уведомления будут видны только здесь.
                </p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-11 sm:h-8"
                  disabled
                >
                  Подключить
                </Button>
              </>
            ) : settings.telegramLinked ? (
              <>
                <p className="text-sm">
                  Telegram:{' '}
                  <span className="font-medium">Подключён</span>
                </p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-11 sm:h-8"
                  disabled={isTelegramPending}
                  aria-busy={isTelegramPending}
                  onClick={() => {
                    void handleUnlinkTelegram()
                  }}
                >
                  {isTelegramPending && (
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                  )}
                  Отключить
                </Button>
              </>
            ) : (
              <>
                <p className="text-sm">
                  Telegram:{' '}
                  <span className="text-muted-foreground">не подключён</span>
                </p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-11 sm:h-8"
                  disabled={isTelegramPending}
                  aria-busy={isTelegramPending}
                  onClick={() => {
                    void handleLinkTelegram()
                  }}
                >
                  {isTelegramPending && (
                    <Loader2
                      data-icon="inline-start"
                      aria-hidden
                      className="animate-spin"
                    />
                  )}
                  Подключить
                </Button>
              </>
            )}

            {telegramLink && !settings.telegramLinked && (
              <div className="space-y-2 rounded-lg border bg-muted/40 p-3">
                <p className="text-sm break-words">
                  Код:{' '}
                  <span className="font-mono text-xs break-all">
                    {telegramLink.code}
                  </span>
                </p>
                <div className="flex flex-wrap items-center gap-2">
                  <a
                    href={telegramLink.deepLink}
                    target="_blank"
                    rel="noreferrer noopener"
                    className={cn(
                      buttonVariants({ variant: 'outline' }),
                      'h-11 sm:h-8'
                    )}
                  >
                    Открыть Telegram
                  </a>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-11 sm:h-8"
                    onClick={handleCheckTelegram}
                  >
                    Проверить
                  </Button>
                </div>
              </div>
            )}

            {telegramError && (
              <p role="alert" className="text-sm text-destructive">
                {telegramError}
              </p>
            )}
          </div>

          {isSaving && (
            <p role="status" className="sr-only">
              Сохраняем настройки…
            </p>
          )}

          {saveError && (
            <p role="alert" className="text-sm text-destructive">
              {saveError}
            </p>
          )}
        </div>
      )}
    </section>
  )
}

function NotificationsList({
  controller,
  leadMinutes,
  onOpen,
}: {
  controller: BarberNotificationsController
  leadMinutes: number
  onOpen: (notification: BarberNotification) => Promise<void>
}) {
  const {
    items,
    status,
    error,
    requiresSignIn,
    offset,
    pageIndex,
    hasPrev,
    hasNext,
    isLoadingPage,
    pageError,
    reload,
    goPrev,
    goNext,
  } = controller
  const listRef = useRef<HTMLDivElement>(null)

  return (
    <section aria-label="Уведомления" className="space-y-3">
      {requiresSignIn ? (
        <div className="flex min-h-[35vh] items-center justify-center py-6">
          <SignInGate
            title="Сессия истекла"
            description="Войдите снова, чтобы увидеть уведомления: cookie-сессия закончилась, пока страница была открыта."
            redirectTo={BARBER_NOTIFICATIONS_PATH}
            onSignedIn={reload}
          />
        </div>
      ) : (
        <>
          <div
            ref={listRef}
            tabIndex={-1}
            aria-busy={status === 'loading'}
            className="space-y-3 rounded-xl outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
          >
            {status === 'loading' && (
              <div className="space-y-2">
                <span className="sr-only">Загружаем уведомления…</span>
                {NOTIFICATION_SKELETON_KEYS.map((key) => (
                  <Skeleton
                    key={key}
                    aria-hidden
                    className="h-16 w-full rounded-xl"
                  />
                ))}
              </div>
            )}

            {status === 'error' && (
              <div className="space-y-3 rounded-xl border bg-card p-4">
                <p role="alert" className="text-sm text-destructive">
                  {error ?? 'Не удалось загрузить уведомления.'}
                </p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-11 sm:h-8"
                  onClick={reload}
                >
                  Повторить
                </Button>
              </div>
            )}

            {status === 'ready' && items.length === 0 && (
              <p className="text-sm text-muted-foreground">
                Уведомлений пока нет.
              </p>
            )}

            {status === 'ready' && items.length > 0 && (
              <ul className="divide-y rounded-xl border bg-card">
                {items.map((notification) => (
                  <NotificationRow
                    key={notification.id}
                    notification={notification}
                    leadMinutes={leadMinutes}
                    onOpen={onOpen}
                  />
                ))}
              </ul>
            )}
          </div>

          <ListPager
            label="Страницы уведомлений"
            rangeLabel={formatPageRange(offset, items.length)}
            hasPrev={hasPrev}
            hasNext={hasNext}
            isLoading={isLoadingPage}
            error={pageError}
            onPrev={goPrev}
            onNext={goNext}
            pageIndex={pageIndex}
            focusTargetRef={listRef}
          />
        </>
      )}
    </section>
  )
}

const NOTIFICATION_SKELETON_KEYS = [
  'notification-skeleton-1',
  'notification-skeleton-2',
  'notification-skeleton-3',
]

function NotificationRow({
  notification,
  leadMinutes,
  onOpen,
}: {
  notification: BarberNotification
  leadMinutes: number
  onOpen: (notification: BarberNotification) => Promise<void>
}) {
  const isUnread = notification.readAt === null

  return (
    <li>
      <button
        type="button"
        className="group flex w-full items-start gap-3 p-4 text-left transition-colors hover:bg-muted/50 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        onClick={() => {
          void onOpen(notification)
        }}
      >
        <span className="min-w-0 flex-1 space-y-1">
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span className="text-xs text-muted-foreground tabular-nums">
              {formatNotificationTimestamp(notification)}
            </span>
            {isUnread && (
              <span
                role="img"
                aria-label="Не прочитано"
                className="size-1.5 shrink-0 rounded-full bg-primary"
              />
            )}
            <span className="text-sm font-medium break-words">
              {notificationTitle(notification, leadMinutes)}
            </span>
          </span>
          <span className="block text-sm break-words text-muted-foreground">
            {notificationBody(notification)}
          </span>
        </span>
        <span className="flex shrink-0 items-center gap-0.5 text-sm text-muted-foreground transition-colors group-hover:text-foreground">
          Открыть
          <ChevronRight className="size-4" aria-hidden />
        </span>
      </button>
    </li>
  )
}

/** «Сегодня, 14:02» в поясе салона; старые строки — с календарной датой. */
function formatNotificationTimestamp(
  notification: BarberNotification
): string {
  const timeZone = notification.salonTimeZoneId ?? undefined
  const day =
    relativeDayLabel(notification.createdAt, Date.now(), timeZone) ??
    formatShortDay(notification.createdAt, timeZone)

  return `${day}, ${formatTime(notification.createdAt, timeZone)}`
}

function notificationTitle(
  notification: BarberNotification,
  leadMinutes: number
): string {
  if (notification.kind === 'Upcoming') {
    return `Через ${leadMinutes} минут запись`
  }

  return `Сегодня ${formatBookingsCount(notification.bookingsCount ?? 0)}`
}

function notificationBody(notification: BarberNotification): string {
  const timeZone = notification.salonTimeZoneId ?? undefined

  if (notification.kind === 'Digest') {
    return notification.startAt === null
      ? ''
      : `Первая в ${formatTime(notification.startAt, timeZone)}`
  }

  const time =
    notification.startAt === null
      ? null
      : formatTime(notification.startAt, timeZone)
  const salon =
    notification.salonName === null ? null : `«${notification.salonName}»`

  return [time, notification.serviceName, salon]
    .filter((part): part is string => part !== null && part !== '')
    .join(' · ')
}
