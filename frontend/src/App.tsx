import { lazy, Suspense, useEffect } from 'react'

import { AppErrorBoundary } from '@/components/error-boundary'
import { SiteFooter } from '@/components/site-footer'
import { SiteHeader } from '@/components/site-header'
import { Skeleton } from '@/components/ui/skeleton'
import { isAdminPath } from '@/lib/admin-path'
import { matchBarberPath } from '@/lib/barber-path'
import { usePathname } from '@/lib/router'
import { HomePage } from '@/pages/home-page'

// Админка, кабинет и карточка барбера — отдельные чанки: витрине не нужен код экранов,
// закрытых ролью, а профиль не должен удорожать первую загрузку (§12.2).
const AdminPage = lazy(() =>
  import('@/pages/admin-page').then((module) => ({ default: module.AdminPage })),
)

const AccountPage = lazy(() =>
  import('@/pages/account-page').then((module) => ({
    default: module.AccountPage,
  })),
)

const BarberPage = lazy(() =>
  import('@/pages/barber-page').then((module) => ({
    default: module.BarberPage,
  })),
)

/**
 * Оболочка приложения: общая шапка и подвал, между ними — страница по адресу.
 *
 * Маршрутов четыре, поэтому вместо роутера хватает `usePathname`. Неизвестный адрес
 * показывает витрину: отдельная страница 404 на четырёх маршрутах была бы шумом.
 *
 * `key` на границе ошибок сбрасывает её состояние при смене страницы: после падения
 * админки ссылка на витрину снова показывает рабочий экран. Для профиля в ключ входит
 * id — иначе падение одного барбера «залипало» бы на следующем.
 */
function App() {
  const pathname = usePathname()
  const barberId = matchBarberPath(pathname)
  const isAdmin = isAdminPath(pathname)
  const pageKey = isAdmin
    ? 'admin'
    : pathname === '/account'
      ? 'account'
      : barberId !== null
        ? `barber:${barberId}`
        : 'home'

  useEffect(() => {
    // Смена верхнеуровневой страницы начинается сверху; выбор отзыва в админке
    // меняет только вложенный путь и не должен прокручивать очередь.
    if (!window.location.hash) {
      window.scrollTo({ top: 0 })
    }
  }, [pageKey])

  return (
    <div className="flex min-h-svh flex-col bg-background">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-background focus:px-3 focus:py-2 focus:text-sm focus:ring-1 focus:ring-border"
      >
        К содержимому
      </a>

      <SiteHeader />

      <main id="main" className="flex-1">
        <AppErrorBoundary key={pageKey}>
          <Suspense fallback={<RouteFallback />}>
            {isAdmin ? (
              <AdminPage />
            ) : pathname === '/account' ? (
              <AccountPage />
            ) : barberId !== null ? (
              <BarberPage id={barberId} />
            ) : (
              <HomePage />
            )}
          </Suspense>
        </AppErrorBoundary>
      </main>

      {!isAdmin && <SiteFooter />}
    </div>
  )
}

/**
 * Заглушка на время загрузки чанка.
 *
 * Тот же ритм, что у скелетонов страниц, и текстовое объявление для скринридера:
 * без него переход по ссылке на медленной сети выглядит как «ничего не произошло».
 */
function RouteFallback() {
  return (
    <div
      role="status"
      aria-live="polite"
      className="mx-auto w-full max-w-5xl px-4 py-8 sm:py-10"
    >
      <span className="sr-only">Загрузка страницы</span>
      <Skeleton className="h-8 w-56" />
      <Skeleton className="mt-6 h-4 w-full max-w-md" />
      <Skeleton className="mt-3 h-4 w-full max-w-sm" />
      <Skeleton className="mt-8 h-40 w-full" />
    </div>
  )
}

export default App
