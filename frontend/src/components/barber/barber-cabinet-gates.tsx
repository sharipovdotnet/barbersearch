import { Bell, CalendarCheck, ShieldAlert, UserX } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useUnreadNotifications } from '@/hooks/use-unread-notifications'
import {
  BARBER_NOTIFICATIONS_PATH,
  BARBER_ONBOARDING_PATH,
} from '@/lib/barber-cabinet-path'
import { isPlainClick, navigate } from '@/lib/router'
import { useI18n } from '@/i18n'

/**
 * Гейты кабинета барбера.
 *
 * Запрос списка уходит только из экрана, который монтируется после проверок:
 * клиент и администратор не должны получать `403`, а барбер без привязанного
 * профиля — `403 auth.forbidden` вместо объяснения.
 */

/** Роль не Barber: честно говорим, что экран не для этой роли. */
export function BarberRoleGate({ role }: { role: string }) {
  const { t } = useI18n()
  const isAdmin = role === 'Admin'

  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <ShieldAlert className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>
            {isAdmin ? t('Вы администратор') : t('Это кабинет барбера')}
          </CardTitle>
          <CardDescription>
            {isAdmin
              ? t(
                  'Модерация отзывов, барберы и пользователи — в отдельном разделе администрирования.'
                )
              : t(
                  'Записи клиентов видит барбер со своим профилем. Ваши записи и отзывы — в кабинете клиента.'
                )}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap items-center gap-2">
          {isAdmin ? (
            <Button
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => {
                navigate('/admin')
              }}
            >
              {t('Открыть администрирование')}
            </Button>
          ) : (
            <Button
              size="sm"
              className="h-11 sm:h-8"
              onClick={() => {
                navigate('/account')
              }}
            >
              {t('Мои записи')}
            </Button>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={() => {
              navigate('/')
            }}
          >
            {t('На главную')}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

/**
 * Барбер без привязанного профиля: сервер закрыл бы его `403`, но это не тупик —
 * профиль создаётся здесь же, на `/barber/onboarding`.
 */
export function BarberProfileMissingGate() {
  const { t } = useI18n()

  return (
    <div className="mx-auto w-full max-w-md">
      <Card>
        <CardHeader>
          <div className="flex size-10 items-center justify-center rounded-full bg-muted">
            <UserX className="size-5 text-muted-foreground" aria-hidden />
          </div>
          <CardTitle>{t('Профиль ещё не создан')}</CardTitle>
          <CardDescription>
            {t(
              'Записи клиентов приходят только для профиля, связанного с вашим аккаунтом. Заполните его сами — это займёт пару минут.'
            )}
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-wrap items-center gap-2">
          <Button
            size="sm"
            className="h-11 sm:h-8"
            onClick={() => {
              navigate(BARBER_ONBOARDING_PATH)
            }}
          >
            {t('Создать профиль барбера')}
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="h-11 sm:h-8"
            onClick={() => {
              navigate('/')
            }}
          >
            {t('На главную')}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

/** Скелетон повторяет силуэт заголовка, вкладок и строк — загрузка не прыгает. */
export function BarberCabinetSkeleton() {
  return (
    <div className="space-y-5" aria-busy="true">
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-4 w-80" />
      </div>
      <div className="space-y-3" aria-hidden>
        <Skeleton className="h-9 w-56 rounded-lg" />
        <ul className="divide-y rounded-xl border bg-card">
          {SKELETON_KEYS.map((key) => (
            <li key={key} className="space-y-2 p-4">
              <Skeleton className="h-4 w-40" />
              <Skeleton className="h-4 w-56" />
              <Skeleton className="h-4 w-full max-w-sm" />
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}

const SKELETON_KEYS = ['barber-skeleton-1', 'barber-skeleton-2', 'barber-skeleton-3']

/**
 * Колокольчик уведомлений: вход в инбокс со всех страниц кабинета.
 *
 * Отдельный пункт навигации не заводится (§8.1): уведомления — утилита, а четвёртую
 * ячейку сетки 2×2 на узком экране занимает раздел «Профиль». Кнопка 44×44, поэтому
 * на узком экране она не сжимается, а заголовок рядом переносится. Счётчик читается
 * своим хуком; пока он неизвестен или упал, бейджа нет и остаётся только имя
 * «Уведомления».
 */
export function BarberUnreadBell() {
  const { t } = useI18n()
  const { count } = useUnreadNotifications()
  const hasUnread = count !== null && count > 0
  const label =
    count === null
      ? t('Уведомления')
      : t('Уведомления, непрочитанных: {count}', { count })

  return (
    <>
      <a
        href={BARBER_NOTIFICATIONS_PATH}
        aria-label={label}
        className="relative inline-flex size-11 shrink-0 items-center justify-center rounded-full border border-border bg-background text-muted-foreground transition-colors hover:text-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        onClick={(event) => {
          if (isPlainClick(event)) {
            event.preventDefault()
            navigate(BARBER_NOTIFICATIONS_PATH)
          }
        }}
      >
        <Bell className="size-5" aria-hidden />
        {hasUnread && (
          <span
            aria-hidden
            className="absolute -top-0.5 -right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-xs leading-none font-medium text-primary-foreground tabular-nums"
          >
            {count > 99 ? '99+' : count}
          </span>
        )}
      </a>
      {/* Смена счётчика объявляется отдельно: aria-label ссылки меняется молча. */}
      <span aria-live="polite" className="sr-only">
        {count === null
          ? ''
          : t('Непрочитанных уведомлений: {count}', { count })}
      </span>
    </>
  )
}

/** Заголовок экрана: один `h1` и подпись, общие для скелетона и списка. */
export function BarberCabinetHeading() {
  const { t } = useI18n()

  return (
    <header className="flex items-start gap-3">
      <div className="flex size-10 shrink-0 items-center justify-center rounded-full bg-muted">
        <CalendarCheck className="size-5 text-muted-foreground" aria-hidden />
      </div>
      <div className="min-w-0 flex-1">
        <h1 className="font-heading text-2xl font-semibold tracking-tight break-words sm:text-3xl">
          {t('Кабинет барбера')}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {t('Записи клиентов: подтверждение, завершение и заметки.')}
        </p>
      </div>
      <BarberUnreadBell />
    </header>
  )
}
