import type { MouseEvent } from 'react'
import { Scissors } from 'lucide-react'

import { HeaderAuth } from '@/components/header-auth'
import { buttonVariants } from '@/components/ui/button'
import { useSession } from '@/hooks/use-session'
import { cn } from '@/lib/utils'
import { goToSection, navigate, usePathname } from '@/lib/router'

/**
 * Общая шапка витрины и кабинета.
 *
 * Секции витрины живут на `/`, поэтому ссылки ведут на `/#barbers`: с кабинета это
 * сначала переход на витрину, потом прокрутка к якорю.
 */
export function SiteHeader() {
  const pathname = usePathname()
  const { status, user } = useSession()
  const isAccount = pathname === '/account'
  const isAuthenticated = status === 'authenticated' && Boolean(user)

  function handleSection(event: MouseEvent, id: string) {
    event.preventDefault()
    goToSection(id)
  }

  return (
    <header className="sticky top-0 z-30 border-b bg-background/85 backdrop-blur">
      <div className="mx-auto flex h-14 max-w-5xl items-center gap-5 px-4 sm:gap-6">
        <a
          href="/"
          className="flex items-center gap-2 font-heading text-base font-semibold tracking-tight"
          onClick={(event) => {
            event.preventDefault()
            navigate('/')
          }}
        >
          <Scissors className="size-4" aria-hidden />
          BarberSearch
        </a>

        <nav
          aria-label="Основная навигация"
          className="hidden items-center gap-4 text-sm text-muted-foreground sm:flex"
        >
          <a
            href="/#barbers"
            className="transition-colors hover:text-foreground"
            onClick={(event) => handleSection(event, 'barbers')}
          >
            Барберы
          </a>
          <a
            href="/#how"
            className="transition-colors hover:text-foreground"
            onClick={(event) => handleSection(event, 'how')}
          >
            Как это работает
          </a>
          {isAuthenticated && (
            <a
              href="/account"
              aria-current={isAccount ? 'page' : undefined}
              className={cn(
                'transition-colors hover:text-foreground',
                isAccount && 'font-medium text-foreground'
              )}
              onClick={(event) => {
                event.preventDefault()
                navigate('/account')
              }}
            >
              Мои записи
            </a>
          )}
        </nav>

        <div className="ml-auto flex items-center gap-1.5 sm:gap-2">
          <HeaderAuth />
          <a
            href="/#barbers"
            className={cn(buttonVariants({ size: 'sm' }), 'hidden sm:inline-flex')}
            onClick={(event) => handleSection(event, 'barbers')}
          >
            {isAccount ? 'К поиску' : 'Найти барбера'}
          </a>
        </div>
      </div>
    </header>
  )
}
