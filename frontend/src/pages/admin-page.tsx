import { useEffect, type ReactNode } from 'react'

import {
  AdminForbidden,
  AdminNotFound,
  AdminPageSkeleton,
  AdminSignInGate,
} from '@/components/admin/admin-gate'
import { AdminRail } from '@/components/admin/admin-rail'
import { BarbersScreen } from '@/components/admin/barbers-screen'
import { CatalogueScreen } from '@/components/admin/catalogue-screen'
import { ReviewsScreen } from '@/components/admin/reviews-screen'
import { UsersScreen } from '@/components/admin/users-screen'
import { SessionUnavailable } from '@/components/session-unavailable'
import { useSession } from '@/hooks/use-session'
import {
  isAdminPath,
  parseAdminPath,
  reviewsPath,
  type AdminRoute,
} from '@/lib/admin-path'
import { navigate, useLocation } from '@/lib/router'

const ADMIN_TITLES = {
  reviews: 'Отзывы — администрирование BarberSearch',
  barbers: 'Барберы — администрирование BarberSearch',
  catalogue: 'Каталог — администрирование BarberSearch',
  users: 'Пользователи — администрирование BarberSearch',
} as const

/** Заголовок вкладки по маршруту: `notFound` показывается как отзывы. */
function titleFor(route: AdminRoute): string {
  switch (route.kind) {
    case 'barbers':
      return ADMIN_TITLES.barbers
    case 'users':
      return ADMIN_TITLES.users
    case 'catalogue':
      return ADMIN_TITLES.catalogue
    default:
      return ADMIN_TITLES.reviews
  }
}

function splitLocation(location: string): { pathname: string; search: string } {
  const index = location.indexOf('?')

  return index === -1
    ? { pathname: location, search: '' }
    : { pathname: location.slice(0, index), search: location.slice(index) }
}

/**
 * Раздел администрирования.
 *
 * Клиентская проверка роли — только UX: действия всё равно закрыты политикой
 * `AdminOnly` на сервере. Аноним видит вход, пользователь без роли — отказ,
 * и данные при этом не запрашиваются.
 */
export function AdminPage() {
  const location = useLocation()
  const { pathname, search } = splitLocation(location)
  const route = parseAdminPath(pathname, search)
  const { status, user } = useSession()
  const tab = route.kind === 'reviews' ? route.tab : 'pending'
  const title = titleFor(route)

  // `/admin` — тот же экран, но канонический адрес ведёт на очередь отзывов.
  useEffect(() => {
    if (pathname !== '/admin') {
      return
    }

    navigate(reviewsPath(tab), { replace: true })
  }, [pathname, tab])

  useEffect(() => {
    document.title = title

    // noindex не защищает данные — API закрыт ролью; это только гигиена выдачи.
    const meta = document.createElement('meta')
    meta.name = 'robots'
    meta.content = 'noindex, nofollow'
    document.head.append(meta)

    return () => meta.remove()
  }, [title])

  if (status === 'loading') {
    return <AdminPageSkeleton />
  }

  // До проверки роли: иначе неизвестная сессия выглядела бы как «Нет доступа».
  if (status === 'unavailable') {
    return (
      <div className="mx-auto flex min-h-[60vh] w-full max-w-md items-center px-4 py-8">
        <SessionUnavailable />
      </div>
    )
  }

  if (status === 'anonymous') {
    return (
      <div className="mx-auto flex min-h-[60vh] w-full max-w-md items-center px-4 py-8">
        <AdminSignInGate returnTo={location} />
      </div>
    )
  }

  if (!user || user.role !== 'Admin') {
    return (
      <div className="mx-auto flex min-h-[60vh] w-full max-w-md items-center px-4 py-8">
        <AdminForbidden />
      </div>
    )
  }

  if (!isAdminPath(pathname) || route.kind === 'notFound') {
    return (
      <AdminShell>
        <AdminNotFound />
      </AdminShell>
    )
  }

  if (route.kind === 'barbers') {
    return (
      <AdminShell hideRailOnMobile={route.barberId !== null}>
        <BarbersScreen barberId={route.barberId} />
      </AdminShell>
    )
  }

  if (route.kind === 'users') {
    return (
      <AdminShell hideRailOnMobile={route.userId !== null}>
        <UsersScreen userId={route.userId} />
      </AdminShell>
    )
  }

  if (route.kind === 'catalogue') {
    return (
      <AdminShell>
        <CatalogueScreen tab={route.tab} />
      </AdminShell>
    )
  }

  return (
    <AdminShell hideRailOnMobile={route.reviewId !== null}>
      <ReviewsScreen tab={route.tab} reviewId={route.reviewId} />
    </AdminShell>
  )
}

function AdminShell({
  children,
  hideRailOnMobile = false,
}: {
  children: ReactNode
  hideRailOnMobile?: boolean
}) {
  return (
    <div className="mx-auto grid w-full max-w-6xl gap-6 px-4 py-6 lg:grid-cols-[13rem_minmax(0,1fr)] lg:items-start lg:py-8">
      {/* `min-w-0` на обоих столбцах: без него горизонтальный рейл растягивает
          grid-элемент по min-content, и страница получает горизонтальный скролл на 320. */}
      <div className={hideRailOnMobile ? 'hidden min-w-0 lg:block' : 'min-w-0'}>
        <AdminRail />
      </div>
      <div className="min-w-0">{children}</div>
    </div>
  )
}
