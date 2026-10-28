import { useEffect } from 'react'

import { SiteFooter } from '@/components/site-footer'
import { SiteHeader } from '@/components/site-header'
import { isAdminPath } from '@/lib/admin-path'
import { usePathname } from '@/lib/router'
import { AccountPage } from '@/pages/account-page'
import { AdminPage } from '@/pages/admin-page'
import { HomePage } from '@/pages/home-page'

/**
 * Оболочка приложения: общая шапка и подвал, между ними — страница по адресу.
 *
 * Маршрутов два, поэтому вместо роутера хватает `usePathname`. Неизвестный адрес
 * показывает витрину: отдельная страница 404 на двух маршрутах была бы шумом.
 */
function App() {
  const pathname = usePathname()
  const isAdmin = isAdminPath(pathname)
  const pageKey = isAdmin ? 'admin' : pathname === '/account' ? 'account' : 'home'

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
        {isAdmin ? (
          <AdminPage />
        ) : pathname === '/account' ? (
          <AccountPage />
        ) : (
          <HomePage />
        )}
      </main>

      {!isAdmin && <SiteFooter />}
    </div>
  )
}

export default App
